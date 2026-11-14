#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Game.Model;
using Game.UnityBridge;
using Game.UnityBridge.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UnityBridge.EditorTools
{
    /// <summary>
    /// 一键生成演示场景:占位材质 → 预制体 → 资源索引 → 场景与 UGUI。
    /// 视觉口径:白色地图 / 白色面板 + 黑色描边;基地为一整段城墙。
    /// 入口:菜单 Tools/防线指挥/一键生成演示场景。
    /// </summary>
    public static class ProjectSetupWizard
    {
        private const string GeneratedDir = "Assets/Unity/Generated";
        private const string MaterialDir = GeneratedDir + "/Materials";
        private const string PrefabDir = GeneratedDir + "/Prefabs";
        private const string DataDir = GeneratedDir + "/Data";
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/Battle.unity";

        private static readonly Color Ink = new Color(0.06f, 0.07f, 0.09f);
        private static readonly Color Muted = new Color(0.38f, 0.42f, 0.47f);
        private static readonly Color Gold = new Color(0.68f, 0.46f, 0.02f);
        private static readonly Color Paper = Color.white;
        private static readonly Color Ground = new Color(0.965f, 0.97f, 0.975f);

        [MenuItem("Tools/防线指挥/一键生成演示场景", false, 0)]
        public static void BuildDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolders();

            ModelDatabase db = DefsJsonLoader.LoadFromStreamingAssets();
            if (db.Towers.Count == 0) return;

            Dictionary<string, Material> materials = CreateMaterials();
            AssetCatalog catalog = CreateUnitPrefabs(db, materials, out GameObject fallbackTower,
                out GameObject enemyFallback, out GameObject projectilePrefab,
                out GameObject droneSquadPrefab, out GameObject areaFieldPrefab,
                out Transform rodPrefab, out GameObject vfxPrefab);
            AudioLibrary audioLibrary = CreateAudioLibrary();

            CreateScene(db, catalog, fallbackTower, enemyFallback, projectilePrefab,
                droneSquadPrefab, areaFieldPrefab, rodPrefab, vfxPrefab, audioLibrary);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "防线指挥",
                "演示场景已生成并保存到:\n" + ScenePath + "\n\n直接点 Play 进入布防阶段。\n" +
                "左键点空位建造;点击塔查看射程与详情/拆除/升级;右键拖动、滚轮缩放。",
                "好的");
        }

        // ---------------------------------------------------------------- 目录

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Unity");
            EnsureFolder("Assets/Unity", "Generated");
            EnsureFolder(GeneratedDir, "Materials");
            EnsureFolder(GeneratedDir, "Prefabs");
            EnsureFolder(GeneratedDir, "Data");
            EnsureFolder(GeneratedDir, "Textures");
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }

        // ---------------------------------------------------------------- 材质

        private static Dictionary<string, Material> CreateMaterials()
        {
            var map = new Dictionary<string, Material>();
            map["ground"] = CreateMaterialAsset("Ground", Ground);
            map["wall"] = CreateMaterialAsset("Wall", new Color(0.13f, 0.14f, 0.16f));
            map["slot_black"] = CreateMaterialAsset("Slot_Black", Ink);
            map["slot_white"] = CreateMaterialAsset("Slot_White", Paper);
            map["tower_base"] = CreateMaterialAsset("Tower_Base", new Color(0.9f, 0.91f, 0.92f));
            map["selected"] = CreateMaterialAsset("Tower_Selected", Ink);
            map["hp"] = CreateMaterialAsset("Enemy_Hp", new Color(0.08f, 0.09f, 0.11f));
            map["projectile"] = CreateMaterialAsset("Projectile", Ink);
            map["burn"] = CreateMaterialAsset("Area_Burn", new Color(0.95f, 0.45f, 0.12f));
            map["chill"] = CreateMaterialAsset("Area_Chill", new Color(0.2f, 0.6f, 0.92f));
            map["rod"] = CreateMaterialAsset("Lightning_Rod", new Color(0.32f, 0.28f, 0.6f));
            map["drone"] = CreateMaterialAsset("Drone", new Color(0.1f, 0.5f, 0.3f));

            map["tower_machine_gun"] = CreateMaterialAsset("Tower_MachineGun", new Color(0.22f, 0.24f, 0.28f));
            map["tower_laser"] = CreateMaterialAsset("Tower_Laser", new Color(0.34f, 0.24f, 0.6f));
            map["tower_mortar"] = CreateMaterialAsset("Tower_Mortar", new Color(0.62f, 0.4f, 0.05f));
            map["tower_rocket"] = CreateMaterialAsset("Tower_Rocket", new Color(0.7f, 0.28f, 0.1f));
            map["tower_tesla"] = CreateMaterialAsset("Tower_Tesla", new Color(0.08f, 0.42f, 0.6f));
            map["tower_drone_hangar"] = CreateMaterialAsset("Tower_DroneHangar", new Color(0.1f, 0.45f, 0.3f));

            map["enemy_small"] = CreateMaterialAsset("Enemy_Small", new Color(0.12f, 0.3f, 0.55f));
            map["enemy_medium"] = CreateMaterialAsset("Enemy_Medium", new Color(0.62f, 0.38f, 0.05f));
            map["enemy_large"] = CreateMaterialAsset("Enemy_Large", new Color(0.6f, 0.1f, 0.15f));
            map["wall_line"] = CreateWallLineMaterial();
            return map;
        }

        /// <summary>线条风城墙贴图:白底 + 黑色垛口与砖缝,平铺在地图底边。</summary>
        private static Material CreateWallLineMaterial()
        {
            string pngPath = GeneratedDir + "/Textures/wall_line.png";

            if (!File.Exists(pngPath))
            {
                const int width = 512;
                const int height = 64;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var pixels = new Color32[width * height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        pixels[y * width + x] = new Color32(255, 255, 255, 255);
                    }
                }

                // 外框
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < 3; y++)
                    {
                        pixels[y * width + x] = new Color32(20, 20, 22, 255);
                        pixels[(height - 1 - y) * width + x] = new Color32(20, 20, 22, 255);
                    }
                }
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        pixels[y * width + x] = new Color32(20, 20, 22, 255);
                        pixels[y * width + (width - 1 - x)] = new Color32(20, 20, 22, 255);
                    }
                }

                // 顶部垛口
                for (int x = 0; x < width; x += 64)
                {
                    for (int y = height - 3; y > height - 18; y--)
                    {
                        for (int dx = 0; dx < 30; dx++)
                        {
                            pixels[y * width + x + dx] = new Color32(20, 20, 22, 255);
                        }
                    }
                }

                // 砖缝
                for (int x = 0; x < width; x += 64)
                {
                    for (int y = 3; y < height - 18; y++)
                    {
                        pixels[y * width + x] = new Color32(20, 20, 22, 255);
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(pngPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            string matPath = MaterialDir + "/Wall_Line.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Unlit/Texture");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, matPath);
            }

            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateMaterialAsset(string name, Color color)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- 单位预制体

        private static AssetCatalog CreateUnitPrefabs(
            ModelDatabase db,
            Dictionary<string, Material> mat,
            out GameObject fallbackTower,
            out GameObject enemyFallback,
            out GameObject projectilePrefab,
            out GameObject droneSquadPrefab,
            out GameObject areaFieldPrefab,
            out Transform rodPrefab,
            out GameObject vfxPrefab)
        {
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            string catalogPath = DataDir + "/AssetCatalog.asset";
            AssetDatabase.DeleteAsset(catalogPath);
            AssetDatabase.CreateAsset(catalog, catalogPath);

            var towerPrefabs = new Dictionary<string, GameObject>();
            fallbackTower = null;

            for (int i = 0; i < db.Towers.Count; i++)
            {
                TowerDef def = db.Towers[i];
                GameObject prefab = BuildTowerPrefab(def, mat);
                towerPrefabs[def.Id] = prefab;
                if (fallbackTower == null) fallbackTower = prefab;
            }

            var enemyPrefabs = new Dictionary<string, GameObject>();
            enemyFallback = null;

            for (int i = 0; i < db.Enemies.Count; i++)
            {
                EnemyDef def = db.Enemies[i];
                GameObject prefab = BuildEnemyPrefab(def, mat);
                enemyPrefabs[def.Id] = prefab;
                if (enemyFallback == null) enemyFallback = prefab;
            }

            projectilePrefab = BuildProjectilePrefab(mat);
            droneSquadPrefab = BuildDroneSquadPrefab(mat);
            areaFieldPrefab = BuildAreaFieldPrefab(mat);
            rodPrefab = BuildRodPrefab(mat);
            vfxPrefab = BuildVfxPrefab(mat);

            var so = new SerializedObject(catalog);
            SerializedProperty towersProp = so.FindProperty("towers");
            towersProp.arraySize = db.Towers.Count;
            for (int i = 0; i < db.Towers.Count; i++)
            {
                SerializedProperty element = towersProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").stringValue = db.Towers[i].Id;
                element.FindPropertyRelative("prefab").objectReferenceValue = towerPrefabs[db.Towers[i].Id];
            }

            SerializedProperty enemiesProp = so.FindProperty("enemies");
            enemiesProp.arraySize = db.Enemies.Count;
            for (int i = 0; i < db.Enemies.Count; i++)
            {
                SerializedProperty element = enemiesProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").stringValue = db.Enemies[i].Id;
                element.FindPropertyRelative("prefab").objectReferenceValue = enemyPrefabs[db.Enemies[i].Id];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static GameObject BuildTowerPrefab(TowerDef def, Dictionary<string, Material> mat)
        {
            Material accent = mat.ContainsKey("tower_" + def.Id) ? mat["tower_" + def.Id] : mat["hp"];

            var root = new GameObject("Tower_" + def.Id);
            var view = root.AddComponent<TowerView>();

            Primitive(PrimitiveType.Cylinder, "Base", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(1.4f, 0.1f, 1.4f), mat["tower_base"]);

            var turret = new GameObject("Turret").transform;
            turret.SetParent(root.transform, false);
            turret.localPosition = new Vector3(0f, 0.3f, 0f);

            Primitive(PrimitiveType.Cube, "Barrel", turret,
                new Vector3(0f, 0.05f, 0.6f), new Vector3(0.18f, 0.18f, 1.2f), accent);
            Primitive(PrimitiveType.Cube, "Breech", turret,
                new Vector3(0f, 0.05f, 0f), new Vector3(0.38f, 0.32f, 0.38f), accent);

            var levelRoot = new GameObject("LevelRoot").transform;
            levelRoot.SetParent(root.transform, false);
            for (int i = 0; i < 6; i++)
            {
                float angle = -150f + i * 24f;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * 0.85f,
                    0.24f,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * 0.85f);
                GameObject pip = Primitive(PrimitiveType.Cube, "Pip" + i, levelRoot,
                    offset, Vector3.one * 0.12f, mat["selected"]);
                pip.SetActive(false);
            }

            GameObject selected = Primitive(PrimitiveType.Cylinder, "Selected", root.transform,
                new Vector3(0f, 0.01f, 0f), new Vector3(1.8f, 0.01f, 1.8f), mat["selected"]);
            selected.SetActive(false);

            SetRef(view, "turret", turret);
            SetRef(view, "levelRoot", levelRoot);
            SetRef(view, "selectedMark", selected.gameObject);
            return SavePrefab(root, "Tower_" + def.Id);
        }

        private static GameObject BuildEnemyPrefab(EnemyDef def, Dictionary<string, Material> mat)
        {
            string sizeKey = def.Size == EnemySize.Large ? "enemy_large"
                : (def.Size == EnemySize.Medium ? "enemy_medium" : "enemy_small");

            var root = new GameObject("Enemy_" + def.Id);
            var view = root.AddComponent<EnemyView>();

            Transform body = Primitive(PrimitiveType.Sphere, "Body", root.transform,
                new Vector3(0f, 0.5f, 0f), Vector3.one * 2f, mat[sizeKey]).transform;

            Transform hpBar = Primitive(PrimitiveType.Cube, "HpBar", root.transform,
                new Vector3(0f, 1.6f, 0f), new Vector3(2f, 0.09f, 0.14f), mat["hp"]).transform;

            var statusRoot = new GameObject("StatusRoot").transform;
            statusRoot.SetParent(root.transform, false);
            statusRoot.gameObject.SetActive(false);

            SetRef(view, "body", body);
            SetRef(view, "hpFill", hpBar);
            SetRef(view, "statusRoot", statusRoot);
            return SavePrefab(root, "Enemy_" + def.Id);
        }

        private static GameObject BuildProjectilePrefab(Dictionary<string, Material> mat)
        {
            var root = new GameObject("Projectile");
            root.AddComponent<ProjectileView>();
            Primitive(PrimitiveType.Sphere, "Ball", root.transform,
                Vector3.zero, Vector3.one * 0.6f, mat["projectile"]);
            return SavePrefab(root, "Projectile");
        }

        private static GameObject BuildDroneSquadPrefab(Dictionary<string, Material> mat)
        {
            var root = new GameObject("DroneSquad");
            root.AddComponent<DroneSquadView>();

            var markers = new GameObject("Markers").transform;
            markers.SetParent(root.transform, false);

            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2f / 8f;
                Primitive(PrimitiveType.Cube, "Marker" + i, markers,
                    new Vector3(Mathf.Cos(angle) * 1f, 0.6f, Mathf.Sin(angle) * 1f),
                    new Vector3(0.28f, 0.12f, 0.42f), mat["drone"]);
            }

            SetRef(root.GetComponent<DroneSquadView>(), "markersRoot", markers);
            return SavePrefab(root, "DroneSquad");
        }

        private static GameObject BuildAreaFieldPrefab(Dictionary<string, Material> mat)
        {
            var root = new GameObject("AreaField");
            var view = root.AddComponent<AreaFieldView>();

            GameObject disc = Primitive(PrimitiveType.Cylinder, "Disc", root.transform,
                new Vector3(0f, 0.02f, 0f), new Vector3(1f, 0.01f, 1f), mat["burn"]);

            SetRef(view, "fieldRenderer", disc.GetComponent<Renderer>());
            return SavePrefab(root, "AreaField");
        }

        private static Transform BuildRodPrefab(Dictionary<string, Material> mat)
        {
            var root = new GameObject("Rod");
            Primitive(PrimitiveType.Cylinder, "Stem", root.transform,
                new Vector3(0f, 0.9f, 0f), new Vector3(0.1f, 0.9f, 0.1f), mat["rod"]);
            Primitive(PrimitiveType.Sphere, "Tip", root.transform,
                new Vector3(0f, 1.9f, 0f), Vector3.one * 0.32f, mat["rod"]);

            GameObject prefab = SavePrefab(root, "Rod");
            return prefab.transform;
        }

        private static GameObject BuildVfxPrefab(Dictionary<string, Material> mat)
        {
            var root = new GameObject("Vfx_Default");
            Primitive(PrimitiveType.Sphere, "Burst", root.transform,
                Vector3.zero, Vector3.one, mat["projectile"]);
            return SavePrefab(root, "Vfx_Default");
        }

        // ---------------------------------------------------------------- 场景

        private static void CreateScene(
            ModelDatabase db,
            AssetCatalog catalog,
            GameObject fallbackTower,
            GameObject enemyFallback,
            GameObject projectilePrefab,
            GameObject droneSquadPrefab,
            GameObject areaFieldPrefab,
            Transform rodPrefab,
            GameObject vfxPrefab,
            AudioLibrary audioLibrary)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            MapDef map = db.Maps[0];

            // 相机:默认只看到城墙与下半张地图,右键拖动 / 滚轮缩放。
            GameObject cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 34f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.88f, 0.89f, 0.9f);
            cameraGo.transform.position = new Vector3(map.BaseX, 100f, 22f);
            cameraGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var rig = cameraGo.AddComponent<CameraRig>();
            SetRef(rig, "targetCamera", camera);
            SetFloat(rig, "defaultSize", 34f);
            SetVector3(rig, "defaultPivot", new Vector3(map.BaseX, 0f, 22f));
            SetVector2(rig, "mapMin", new Vector2(0f, 0f));
            SetVector2(rig, "mapMax", new Vector2(map.Width, map.Height));

            // 世界
            var worldRoot = new GameObject("World").transform;

            Primitive(PrimitiveType.Plane, "Ground", worldRoot,
                new Vector3(map.Width * 0.5f, 0f, map.Height * 0.5f),
                new Vector3(map.Width / 10f, 1f, map.Height / 10f),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Ground.mat"));

            BuildBorder(worldRoot, map);
            BuildWall(worldRoot, map);
            BuildSlots(worldRoot, map);

            // GameRoot
            var gameRoot = new GameObject("GameRoot");
            var driver = gameRoot.AddComponent<BattleDriver>();
            var viewRoot = gameRoot.AddComponent<BattleViewRoot>();
            var binder = gameRoot.AddComponent<BattleViewBinder>();
            var audio = gameRoot.AddComponent<AudioService>();
            var picker = gameRoot.AddComponent<BattleSlotPicker>();
            var ui = gameRoot.AddComponent<BattleUiController>();

            var vfxRoot = new GameObject("Vfx").transform;
            vfxRoot.SetParent(gameRoot.transform, false);
            var vfxSink = vfxRoot.gameObject.AddComponent<BattleVfxSink>();

            Set(driver, "difficultyId", "normal");
            Set(driver, "mapId", map.Id);
            SetInt(driver, "maxWaves", 30);
            SetInt(driver, "seed", 12345);
            SetBool(driver, "autoPlay", false);
            SetBool(driver, "manualWaveStart", true);
            SetBool(driver, "autoStart", true);
            SetInt(driver, "speedMultiplier", 1);

            SetRef(viewRoot, "catalog", catalog);
            SetRef(viewRoot, "towerPrefab", fallbackTower.GetComponent<TowerView>());
            SetRef(viewRoot, "enemyPrefab", enemyFallback.GetComponent<EnemyView>());
            SetRef(viewRoot, "projectilePrefab", projectilePrefab.GetComponent<ProjectileView>());
            SetRef(viewRoot, "droneSquadPrefab", droneSquadPrefab.GetComponent<DroneSquadView>());
            SetRef(viewRoot, "areaFieldPrefab", areaFieldPrefab.GetComponent<AreaFieldView>());
            SetRef(viewRoot, "rodPrefab", rodPrefab);
            SetRef(viewRoot, "vfxSink", vfxSink);
            SetFloat(viewRoot, "unitScale", 1f);

            SetRef(binder, "driver", driver);
            SetRef(binder, "viewRoot", viewRoot);

            SetRef(audio, "library", audioLibrary);
            SetRef(vfxSink, "root", vfxRoot);
            SetFloat(vfxSink, "unitScale", 1f);
            ConfigureVfxFallback(vfxSink);

            // UGUI
            Canvas canvas = BuildCanvas(out GameObject canvasGo);

            Text goldText, hpText, waveText, statusText;
            Image hpFill;
            Button pauseButton, speedButton, startWaveButton;
            Text pauseLabel, speedLabel, startWaveLabel;

            BuildHud(canvas.transform,
                out goldText, out hpText, out waveText, out statusText, out hpFill,
                out startWaveButton, out startWaveLabel, out speedButton, out speedLabel,
                out pauseButton, out pauseLabel);

            var hud = canvasGo.AddComponent<BattleHudView>();
            SetRef(hud, "driver", driver);
            SetRef(hud, "goldText", goldText);
            SetRef(hud, "baseHpText", hpText);
            SetRef(hud, "waveText", waveText);
            SetRef(hud, "statusText", statusText);
            SetRef(hud, "baseHpFill", hpFill);
            SetRef(hud, "pauseButton", pauseButton);
            SetRef(hud, "speedButton", speedButton);
            SetRef(hud, "startWaveButton", startWaveButton);
            SetRef(hud, "pauseLabel", pauseLabel);
            SetRef(hud, "speedLabel", speedLabel);
            SetRef(hud, "startWaveLabel", startWaveLabel);

            SetRef(ui, "driver", driver);
            SetRef(ui, "viewRoot", viewRoot);
            SetRef(ui, "worldCamera", camera);
            SetRef(ui, "canvas", canvas);

            SetRef(picker, "worldCamera", camera);
            SetRef(picker, "driver", driver);
            SetRef(picker, "viewRoot", viewRoot);
            SetRef(picker, "controller", ui);
            SetFloat(picker, "pickRadius", 3.8f);

            Text hint = UiText("Hint", canvas.transform,
                "左键点击空位建造 · 点击防御塔查看详情/拆除/升级 · 右键拖动平移 · 滚轮缩放",
                15, TextAnchor.MiddleCenter, Muted);
            SetRect(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-360f, 30f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();
        }

        private static void BuildBorder(Transform parent, MapDef map)
        {
            Material border = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Wall.mat");

            // 细线条边界:只保留左右与上边,底边由城墙贴图接管。
            Primitive(PrimitiveType.Plane, "BorderUnder", parent,
                new Vector3(map.Width * 0.5f, -0.05f, map.Height * 0.5f),
                new Vector3((map.Width + 1.5f) / 10f, 1f, (map.Height + 1.5f) / 10f), border);

            Primitive(PrimitiveType.Cube, "BorderLeft", parent,
                new Vector3(-0.2f, 0.15f, map.Height * 0.5f),
                new Vector3(0.4f, 0.3f, map.Height), border);
            Primitive(PrimitiveType.Cube, "BorderRight", parent,
                new Vector3(map.Width + 0.2f, 0.15f, map.Height * 0.5f),
                new Vector3(0.4f, 0.3f, map.Height), border);
            Primitive(PrimitiveType.Cube, "BorderTop", parent,
                new Vector3(map.Width * 0.5f, 0.15f, map.Height + 0.2f),
                new Vector3(map.Width + 0.8f, 0.3f, 0.4f), border);
        }

        private static void BuildWall(Transform parent, MapDef map)
        {
            // 贴地城墙:不再使用有高度的 3D 墙体,避免挡住 UI。
            Material wall = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Wall_Line.mat");
            Material frame = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Wall.mat");

            Primitive(PrimitiveType.Plane, "Wall", parent,
                new Vector3(map.BaseX, 0.06f, 1.2f),
                new Vector3(map.Width / 10f, 1f, 0.24f), wall);

            // 实线框:上下左右四条细线,保证与浅灰背景有明确区分。
            Primitive(PrimitiveType.Cube, "WallEdgeTop", parent,
                new Vector3(map.BaseX, 0.1f, 2.42f),
                new Vector3(map.Width, 0.06f, 0.12f), frame);
            Primitive(PrimitiveType.Cube, "WallEdgeBottom", parent,
                new Vector3(map.BaseX, 0.1f, -0.02f),
                new Vector3(map.Width, 0.06f, 0.12f), frame);
            Primitive(PrimitiveType.Cube, "WallEdgeLeft", parent,
                new Vector3(0.06f, 0.1f, 1.2f),
                new Vector3(0.12f, 0.06f, 2.52f), frame);
            Primitive(PrimitiveType.Cube, "WallEdgeRight", parent,
                new Vector3(map.Width - 0.06f, 0.1f, 1.2f),
                new Vector3(0.12f, 0.06f, 2.52f), frame);
        }

        private static void BuildSlots(Transform parent, MapDef map)
        {
            Material black = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Slot_Black.mat");
            Material white = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Slot_White.mat");

            var slotsRoot = new GameObject("Slots").transform;
            slotsRoot.SetParent(parent, false);

            for (int i = 0; i < map.Slots.Count; i++)
            {
                SlotDef slot = map.Slots[i];
                Primitive(PrimitiveType.Cylinder, "Slot" + i, slotsRoot,
                    new Vector3(slot.X, 0.03f, slot.Y), new Vector3(3.4f, 0.02f, 3.4f), black);
                Primitive(PrimitiveType.Cylinder, "SlotFill" + i, slotsRoot,
                    new Vector3(slot.X, 0.05f, slot.Y), new Vector3(2.9f, 0.02f, 2.9f), white);
            }
        }

        private static void ConfigureVfxFallback(BattleVfxSink sink)
        {
            // 不再播放默认的实体爆炸球,只保留落点空心圆,避免留下"阴影"。
            var so = new SerializedObject(sink);
            SerializedProperty fallback = so.FindProperty("fallback");
            fallback.FindPropertyRelative("id").stringValue = "default";
            fallback.FindPropertyRelative("prefab").objectReferenceValue = null;
            fallback.FindPropertyRelative("lifetime").floatValue = 0.1f;
            fallback.FindPropertyRelative("scalePerRadius").floatValue = 0f;
            so.FindProperty("entries").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- UGUI

        private static Canvas BuildCanvas(out GameObject canvasGo)
        {
            canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
            return canvas;
        }

        private static void BuildHud(
            Transform canvas,
            out Text goldText,
            out Text hpText,
            out Text waveText,
            out Text statusText,
            out Image hpFill,
            out Button startWaveButton,
            out Text startWaveLabel,
            out Button speedButton,
            out Text speedLabel,
            out Button pauseButton,
            out Text pauseLabel)
        {
            Image bar = Panel("HUD", canvas, Paper);
            SetRect(bar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 74f));

            Text brand = UiText("Brand", bar.transform, "防线指挥", 24, TextAnchor.MiddleLeft, Ink);
            SetRect(brand.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(200f, 46f));

            goldText = LabeledValue(bar.transform, "Gold", "金币", "300", 190f, Gold);
            hpText = LabeledValue(bar.transform, "BaseHp", "城墙", "15000", 330f, Ink);
            waveText = LabeledValue(bar.transform, "Wave", "波次", "WAVE 1", 470f, Ink);
            statusText = LabeledValue(bar.transform, "Status", "状态", "布防阶段", 610f, Muted);

            Image hpBg = Panel("HpBg", bar.transform, new Color(0.9f, 0.91f, 0.92f));
            SetRect(hpBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(760f, 0f), new Vector2(180f, 12f));

            hpFill = Panel("HpFill", hpBg.transform, Ink);
            Stretch(hpFill.rectTransform);
            hpFill.sprite = BuiltinUiSprite();
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillOrigin = 0;
            hpFill.fillAmount = 1f;

            startWaveButton = UiButton("StartWave", bar.transform, "开始交战", 17);
            SetRect(startWaveButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-340f, 0f),
                new Vector2(130f, 40f));
            startWaveLabel = startWaveButton.GetComponentInChildren<Text>();

            speedButton = UiButton("SpeedButton", bar.transform, "速度 ×1");
            SetRect(speedButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-220f, 0f),
                new Vector2(108f, 38f));
            speedLabel = speedButton.GetComponentInChildren<Text>();

            pauseButton = UiButton("PauseButton", bar.transform, "暂停");
            SetRect(pauseButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-104f, 0f),
                new Vector2(88f, 38f));
            pauseLabel = pauseButton.GetComponentInChildren<Text>();
        }

        private static Text LabeledValue(Transform parent, string name, string label, string value,
            float x, Color color)
        {
            Text labelText = UiText(name + "Label", parent, label, 12, TextAnchor.UpperLeft, Muted);
            SetRect(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(x, 15f), new Vector2(110f, 20f));

            Text valueText = UiText(name, parent, value, 21, TextAnchor.UpperLeft, color);
            SetRect(valueText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(x, -7f), new Vector2(130f, 28f));
            return valueText;
        }

        // ---------------------------------------------------------------- UI 工具

        private static Font _font;

        private static Font UiFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        private static Sprite BuiltinUiSprite()
        {
            return Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image Panel(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            AddBorder(image, 2f);
            return image;
        }

        private static void AddBorder(Graphic graphic, float width)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(width, -width);
            outline.useGraphicAlpha = false;
        }

        private static Text UiText(string name, Transform parent, string content, int size,
            TextAnchor anchor, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont();
            text.fontSize = size;
            text.text = content;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button UiButton(string name, Transform parent, string label, int fontSize = 16)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Paper;
            AddBorder(image, 2f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.94f, 0.96f);
            colors.pressedColor = new Color(0.84f, 0.87f, 0.9f);
            colors.disabledColor = new Color(0.8f, 0.82f, 0.84f);
            button.colors = colors;

            Text text = UiText("Label", rect, label, fontSize, TextAnchor.MiddleCenter, Ink);
            Stretch(text.rectTransform);
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ---------------------------------------------------------------- 序列化赋值

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("[Wizard] 未找到字段:" + target.GetType().Name + "." + field);
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector3(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector2(Object target, string field, Vector2 value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.vector2Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- 其它工具

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            return go;
        }

        private static GameObject SavePrefab(GameObject root, string fileName)
        {
            string path = PrefabDir + "/" + fileName + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static AudioLibrary CreateAudioLibrary()
        {
            string path = DataDir + "/AudioLibrary.asset";
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(path);
            if (library != null) return library;

            library = ScriptableObject.CreateInstance<AudioLibrary>();
            AssetDatabase.CreateAsset(library, path);
            return library;
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath) return;
            }
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
