#if UNITY_EDITOR
using System.Collections.Generic;
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
    /// 全程不依赖任何美术资源,首次打开工程点一次菜单即可运行。
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

        private static readonly Color PanelColor = new Color(0.05f, 0.08f, 0.13f, 0.92f);
        private static readonly Color CardColor = new Color(0.08f, 0.13f, 0.2f, 0.96f);
        private static readonly Color TextColor = new Color(0.86f, 0.91f, 0.96f);
        private static readonly Color MutedColor = new Color(0.5f, 0.58f, 0.67f);
        private static readonly Color GoldColor = new Color(1f, 0.82f, 0.4f);

        [MenuItem("Tools/防线指挥/一键生成演示场景", false, 0)]
        public static void BuildDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolders();

            ModelDatabase db = DefsJsonLoader.LoadFromStreamingAssets();
            if (db.Towers.Count == 0) return;

            Dictionary<string, Material> materials = CreateMaterials();
            GameObject cardPrefab = CreateTowerCardPrefab();
            GameObject nodePrefab = CreateUpgradeNodePrefab();
            AssetCatalog catalog = CreateUnitPrefabs(db, materials, out GameObject fallbackTower,
                out GameObject enemyFallback, out GameObject projectilePrefab,
                out GameObject droneSquadPrefab, out GameObject areaFieldPrefab,
                out Transform rodPrefab, out GameObject vfxPrefab);
            AudioLibrary audioLibrary = CreateAudioLibrary();

            CreateScene(db, catalog, cardPrefab, nodePrefab, fallbackTower, enemyFallback,
                projectilePrefab, droneSquadPrefab, areaFieldPrefab, rodPrefab, vfxPrefab, audioLibrary);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "防线指挥",
                "演示场景已生成并保存到:\n" + ScenePath + "\n\n直接点 Play 即可开始。\n" +
                "操作:点左侧塔卡 → 点战场上的圆形槽位建造;点已建塔查看升级。",
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
            map["panel"] = CreateMaterialAsset("UI_Panel", PanelColor);
            map["tower_base"] = CreateMaterialAsset("Tower_Base", new Color(0.17f, 0.21f, 0.27f));
            map["slot"] = CreateMaterialAsset("Slot_Marker", new Color(0.16f, 0.24f, 0.33f));
            map["selected"] = CreateMaterialAsset("Tower_Selected", new Color(0.22f, 0.88f, 1f));
            map["hp"] = CreateMaterialAsset("Hp_Bar", new Color(0.33f, 0.9f, 0.65f));
            map["projectile"] = CreateMaterialAsset("Projectile", new Color(1f, 0.72f, 0.3f));
            map["burn"] = CreateMaterialAsset("Area_Burn", new Color(1f, 0.42f, 0.12f));
            map["chill"] = CreateMaterialAsset("Area_Chill", new Color(0.35f, 0.8f, 1f));
            map["rod"] = CreateMaterialAsset("Lightning_Rod", new Color(0.66f, 0.55f, 1f));
            map["drone"] = CreateMaterialAsset("Drone", new Color(0.33f, 0.9f, 0.65f));
            map["ground"] = CreateMaterialAsset("Ground", new Color(0.045f, 0.07f, 0.1f));

            map["tower_machine_gun"] = CreateMaterialAsset("Tower_MachineGun", new Color(0.85f, 0.8f, 0.66f));
            map["tower_laser"] = CreateMaterialAsset("Tower_Laser", new Color(0.66f, 0.55f, 1f));
            map["tower_mortar"] = CreateMaterialAsset("Tower_Mortar", new Color(1f, 0.71f, 0.27f));
            map["tower_rocket"] = CreateMaterialAsset("Tower_Rocket", new Color(1f, 0.54f, 0.36f));
            map["tower_tesla"] = CreateMaterialAsset("Tower_Tesla", new Color(0.49f, 0.88f, 1f));
            map["tower_drone_hangar"] = CreateMaterialAsset("Tower_DroneHangar", new Color(0.33f, 0.9f, 0.65f));

            map["enemy_small"] = CreateMaterialAsset("Enemy_Small", new Color(0.49f, 0.88f, 1f));
            map["enemy_medium"] = CreateMaterialAsset("Enemy_Medium", new Color(1f, 0.71f, 0.27f));
            map["enemy_large"] = CreateMaterialAsset("Enemy_Large", new Color(1f, 0.33f, 0.44f));
            return map;
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

            // 写入索引表。
            var so = new SerializedObject(catalog);
            SerializedProperty towersProp = so.FindProperty("towers");
            towersProp.arraySize = db.Towers.Count;
            for (int i = 0; i < db.Towers.Count; i++)
            {
                SerializedProperty element = towersProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").stringValue = db.Towers[i].Id;
                element.FindPropertyRelative("prefab").objectReferenceValue =
                    LoadPrefab(towerPrefabs[db.Towers[i].Id]);
            }

            SerializedProperty enemiesProp = so.FindProperty("enemies");
            enemiesProp.arraySize = db.Enemies.Count;
            for (int i = 0; i < db.Enemies.Count; i++)
            {
                SerializedProperty element = enemiesProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").stringValue = db.Enemies[i].Id;
                element.FindPropertyRelative("prefab").objectReferenceValue =
                    LoadPrefab(enemyPrefabs[db.Enemies[i].Id]);
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
                new Vector3(0f, 0.12f, 0f), new Vector3(1.9f, 0.12f, 1.9f), mat["tower_base"]);

            var turret = new GameObject("Turret").transform;
            turret.SetParent(root.transform, false);
            turret.localPosition = new Vector3(0f, 0.36f, 0f);

            Primitive(PrimitiveType.Cube, "Barrel", turret,
                new Vector3(0f, 0.05f, 0.85f), new Vector3(0.22f, 0.22f, 1.7f), accent);
            Primitive(PrimitiveType.Cube, "Breech", turret,
                new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.4f, 0.5f), accent);

            var levelRoot = new GameObject("LevelRoot").transform;
            levelRoot.SetParent(root.transform, false);
            for (int i = 0; i < 6; i++)
            {
                float angle = -150f + i * 24f;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * 1.15f,
                    0.3f,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * 1.15f);
                GameObject pip = Primitive(PrimitiveType.Cube, "Pip" + i, levelRoot,
                    offset, Vector3.one * 0.16f, accent);
                pip.SetActive(false);
            }

            GameObject selected = Primitive(PrimitiveType.Cylinder, "Selected", root.transform,
                new Vector3(0f, 0.01f, 0f), new Vector3(2.5f, 0.01f, 2.5f), mat["selected"]);
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
            root.transform.position = Vector3.zero;

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

        // ---------------------------------------------------------------- UI 预制体

        private static GameObject CreateTowerCardPrefab()
        {
            var root = new GameObject("TowerCard", typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 84f);

            var background = root.AddComponent<Image>();
            background.color = CardColor;
            var button = root.AddComponent<Button>();
            button.targetGraphic = background;
            var view = root.AddComponent<TowerCardView>();

            Image icon = Panel("Icon", root.transform, Color.white);
            SetRect(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(64f, 64f));

            Text name = UiText("Name", root.transform, "塔名", 16, TextAnchor.UpperLeft, TextColor);
            SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(84f, -8f), new Vector2(190f, 24f));

            Text role = UiText("Role", root.transform, "定位", 11, TextAnchor.UpperLeft, MutedColor);
            SetRect(role.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(84f, -34f), new Vector2(190f, 20f));

            Text price = UiText("Price", root.transform, "0", 15, TextAnchor.UpperRight, GoldColor);
            SetRect(price.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-12f, -8f), new Vector2(80f, 24f));

            Text dps = UiText("Dps", root.transform, "DPS", 11, TextAnchor.LowerRight, MutedColor);
            SetRect(dps.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-12f, 8f), new Vector2(110f, 20f));

            Image mask = Panel("AffordabilityMask", root.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(mask.rectTransform);

            SetRef(view, "icon", icon);
            SetRef(view, "nameText", name);
            SetRef(view, "roleText", role);
            SetRef(view, "priceText", price);
            SetRef(view, "dpsText", dps);
            SetRef(view, "button", button);
            SetRef(view, "affordabilityMask", mask);

            return SavePrefab(root, "TowerCard");
        }

        private static GameObject CreateUpgradeNodePrefab()
        {
            var root = new GameObject("UpgradeNode", typeof(RectTransform));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 96f);

            var background = root.AddComponent<Image>();
            background.color = CardColor;
            var button = root.AddComponent<Button>();
            button.targetGraphic = background;
            var view = root.AddComponent<UpgradeNodeView>();

            Text name = UiText("Name", root.transform, "节点", 14, TextAnchor.UpperLeft, TextColor);
            SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(10f, -8f), new Vector2(230f, 22f));

            Text level = UiText("Level", root.transform, "Lv2", 11, TextAnchor.UpperRight, MutedColor);
            SetRect(level.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-10f, -8f), new Vector2(60f, 20f));

            Text mods = UiText("Modifiers", root.transform, "", 10, TextAnchor.UpperLeft, MutedColor);
            SetRect(mods.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 1f), new Vector2(10f, -34f), new Vector2(-20f, 38f));
            mods.horizontalOverflow = HorizontalWrapMode.Wrap;
            mods.verticalOverflow = VerticalWrapMode.Truncate;

            Text cost = UiText("Cost", root.transform, "0 金币", 11, TextAnchor.LowerLeft, GoldColor);
            SetRect(cost.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(10f, 8f), new Vector2(120f, 20f));

            Image badge = Panel("UniqueBadge", root.transform, new Color(1f, 0.71f, 0.27f, 0.9f));
            SetRect(badge.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-10f, 8f), new Vector2(42f, 20f));
            Text badgeText = UiText("Label", badge.transform, "唯一", 10, TextAnchor.MiddleCenter,
                new Color(0.1f, 0.08f, 0.05f));
            Stretch(badgeText.rectTransform);
            badge.gameObject.SetActive(false);

            SetRef(view, "nameText", name);
            SetRef(view, "levelText", level);
            SetRef(view, "modifierText", mods);
            SetRef(view, "costText", cost);
            SetRef(view, "uniqueBadge", badge.gameObject);
            SetRef(view, "button", button);

            return SavePrefab(root, "UpgradeNode");
        }

        // ---------------------------------------------------------------- 场景

        private static void CreateScene(
            ModelDatabase db,
            AssetCatalog catalog,
            GameObject cardPrefab,
            GameObject nodePrefab,
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

            GameObject cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 66f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.04f, 0.07f);
            cameraGo.transform.position = new Vector3(40f, 100f, 60f);
            cameraGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // 左右两侧留给 UI 面板,中间渲染战场。
            camera.rect = new Rect(0.16f, 0f, 0.68f, 1f);

            // 世界根:地面 + 槽位标记
            var worldRoot = new GameObject("World").transform;
            var slotsRoot = new GameObject("Slots").transform;
            slotsRoot.SetParent(worldRoot, false);

            // 地面(Plane 原始尺寸 10×10;地图 80×120 → 缩放 8×12)
            Primitive(PrimitiveType.Plane, "Ground", worldRoot,
                Vector3.zero, new Vector3(8f, 1f, 12f),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Ground.mat"));

            for (int i = 0; i < db.Maps[0].Slots.Count; i++)
            {
                SlotDef slot = db.Maps[0].Slots[i];
                Primitive(PrimitiveType.Cylinder, "Slot" + i, slotsRoot,
                    new Vector3(slot.X, 0.02f, slot.Y), new Vector3(3.6f, 0.02f, 3.6f), SlotMaterial());
            }

            // 基地标记
            Primitive(PrimitiveType.Cylinder, "Base", worldRoot,
                new Vector3(db.Maps[0].BaseX, 0.15f, db.Maps[0].BaseY),
                new Vector3(5f, 0.15f, 5f),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Tower_Selected.mat"));

            // GameRoot 与运行时组件
            var gameRoot = new GameObject("GameRoot");
            var driver = gameRoot.AddComponent<BattleDriver>();
            var viewRoot = gameRoot.AddComponent<BattleViewRoot>();
            var binder = gameRoot.AddComponent<BattleViewBinder>();
            var audio = gameRoot.AddComponent<AudioService>();
            var picker = gameRoot.AddComponent<BattleSlotPicker>();
            var controller = gameRoot.AddComponent<BuildController>();

            var vfxRoot = new GameObject("Vfx").transform;
            vfxRoot.SetParent(gameRoot.transform, false);
            var vfxSink = vfxRoot.gameObject.AddComponent<BattleVfxSink>();

            Set(driver, "difficultyId", "normal");
            Set(driver, "mapId", db.Maps[0].Id);
            SetInt(driver, "maxWaves", 30);
            SetInt(driver, "seed", 12345);
            SetBool(driver, "autoPlay", false);
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
            ConfigureVfxFallback(vfxSink, vfxPrefab);

            // UGUI
            Canvas canvas = BuildCanvas(out GameObject canvasGo);
            Text goldText, hpText, waveText, statusText;
            Image hpFill;
            Button pauseButton, speedButton;
            Text pauseLabel, speedLabel;

            BuildHud(canvas.transform, out goldText, out hpText, out waveText, out statusText,
                out hpFill, out pauseButton, out speedButton, out pauseLabel, out speedLabel);

            Image buildPanel = Panel("BuildPanel", canvas.transform, PanelColor);
            SetRect(buildPanel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(10f, -32f), new Vector2(300f, -84f));
            Text buildTitle = UiText("Title", buildPanel.transform, "建造", 13,
                TextAnchor.UpperLeft, MutedColor);
            SetRect(buildTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(200f, 22f));

            RectTransform buildContent = CreateContent(buildPanel.transform, 74f);
            var buildPanelView = buildPanel.gameObject.AddComponent<BuildPanelView>();
            SetRef(buildPanelView, "driver", driver);
            SetRef(buildPanelView, "content", buildContent);
            SetRef(buildPanelView, "cardPrefab", cardPrefab.GetComponent<TowerCardView>());
            SetRef(buildPanelView, "catalog", catalog);

            Image upgradePanel = Panel("UpgradePanel", canvas.transform, PanelColor);
            SetRect(upgradePanel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(-10f, -32f), new Vector2(360f, -84f));

            Text towerName = UiText("TowerName", upgradePanel.transform, "未选中", 16,
                TextAnchor.UpperLeft, TextColor);
            SetRect(towerName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(-80f, 24f));

            Text levelText = UiText("Level", upgradePanel.transform, "", 12,
                TextAnchor.UpperRight, MutedColor);
            SetRect(levelText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(70f, 20f));

            Text mechanicText = UiText("Mechanic", upgradePanel.transform,
                "选择战场上的防御塔查看升级树。", 11, TextAnchor.UpperLeft,
                new Color(0.72f, 0.84f, 0.93f));
            SetRect(mechanicText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 1f), new Vector2(14f, -42f), new Vector2(-28f, 56f));
            mechanicText.horizontalOverflow = HorizontalWrapMode.Wrap;
            mechanicText.verticalOverflow = VerticalWrapMode.Truncate;

            RectTransform upgradeContent = CreateContent(upgradePanel.transform, 108f);
            var upgradePanelView = upgradePanel.gameObject.AddComponent<UpgradePanelView>();
            SetRef(upgradePanelView, "driver", driver);
            SetRef(upgradePanelView, "viewRoot", viewRoot);
            SetRef(upgradePanelView, "towerNameText", towerName);
            SetRef(upgradePanelView, "levelText", levelText);
            SetRef(upgradePanelView, "mechanicText", mechanicText);
            SetRef(upgradePanelView, "content", upgradeContent);
            SetRef(upgradePanelView, "nodePrefab", nodePrefab.GetComponent<UpgradeNodeView>());

            var hud = canvasGo.AddComponent<BattleHudView>();
            SetRef(hud, "driver", driver);
            SetRef(hud, "goldText", goldText);
            SetRef(hud, "baseHpText", hpText);
            SetRef(hud, "waveText", waveText);
            SetRef(hud, "statusText", statusText);
            SetRef(hud, "baseHpFill", hpFill);
            SetRef(hud, "pauseButton", pauseButton);
            SetRef(hud, "speedButton", speedButton);
            SetRef(hud, "pauseLabel", pauseLabel);
            SetRef(hud, "speedLabel", speedLabel);

            SetRef(controller, "driver", driver);
            SetRef(controller, "buildPanel", buildPanelView);
            SetRef(controller, "upgradePanel", upgradePanelView);
            SetRef(controller, "viewRoot", viewRoot);
            SetRef(picker, "worldCamera", camera);
            SetRef(picker, "driver", driver);
            SetRef(picker, "viewRoot", viewRoot);
            SetRef(picker, "controller", controller);
            SetFloat(picker, "pickRadius", 5f);

            // 底部提示
            Text hint = UiText("Hint", canvas.transform,
                "点击左侧塔卡 → 点击战场上的圆形槽位建造；点击已建塔查看升级", 13,
                TextAnchor.MiddleCenter, new Color(0.55f, 0.66f, 0.76f));
            SetRect(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-420f, 26f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();
        }

        private static void ConfigureVfxFallback(BattleVfxSink sink, GameObject vfxPrefab)
        {
            var so = new SerializedObject(sink);
            SerializedProperty fallback = so.FindProperty("fallback");
            fallback.FindPropertyRelative("id").stringValue = "default";
            fallback.FindPropertyRelative("prefab").objectReferenceValue = vfxPrefab;
            fallback.FindPropertyRelative("lifetime").floatValue = 0.35f;
            fallback.FindPropertyRelative("scalePerRadius").floatValue = 0.35f;
            so.FindProperty("entries").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

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
            out Button pauseButton,
            out Button speedButton,
            out Text pauseLabel,
            out Text speedLabel)
        {
            Image bar = Panel("HUD", canvas, PanelColor);
            SetRect(bar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 64f));

            Text brand = UiText("Brand", bar.transform, "防线指挥", 20, TextAnchor.MiddleLeft, TextColor);
            SetRect(brand.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(160f, 40f));

            goldText = LabeledValue(bar.transform, "Gold", "金币", "300", 190f, GoldColor);
            hpText = LabeledValue(bar.transform, "BaseHp", "基地", "15000", 330f, new Color(0.33f, 0.9f, 0.65f));
            waveText = LabeledValue(bar.transform, "Wave", "波次", "WAVE 1", 470f, new Color(0.49f, 0.88f, 1f));
            statusText = LabeledValue(bar.transform, "Status", "状态", "部署阶段", 610f, MutedColor);

            Image hpBg = Panel("HpBg", bar.transform, new Color(0.1f, 0.14f, 0.19f, 0.9f));
            SetRect(hpBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(760f, 0f), new Vector2(180f, 10f));
            hpFill = Panel("HpFill", hpBg.transform, new Color(0.33f, 0.9f, 0.65f));
            Stretch(hpFill.rectTransform);
            hpFill.sprite = BuiltinUiSprite();
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillOrigin = 0;
            hpFill.fillAmount = 1f;

            speedButton = UiButton("SpeedButton", bar.transform, "速度 ×1");
            SetRect(speedButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-230f, 0f), new Vector2(110f, 38f));
            speedLabel = speedButton.GetComponentInChildren<Text>();

            pauseButton = UiButton("PauseButton", bar.transform, "暂停");
            SetRect(pauseButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-110f, 0f), new Vector2(90f, 38f));
            pauseLabel = pauseButton.GetComponentInChildren<Text>();
        }

        private static Text LabeledValue(Transform parent, string name, string label, string value,
            float x, Color color)
        {
            Text labelText = UiText(name + "Label", parent, label, 10, TextAnchor.UpperLeft, MutedColor);
            SetRect(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(x, 12f), new Vector2(100f, 18f));

            Text valueText = UiText(name, parent, value, 17, TextAnchor.UpperLeft, color);
            SetRect(valueText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(x, -6f), new Vector2(120f, 24f));
            return valueText;
        }

        private static RectTransform CreateContent(Transform parent, float topOffset)
        {
            RectTransform content = CreateRect("Content", parent);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.offsetMin = new Vector2(8f, 8f);
            content.offsetMax = new Vector2(-8f, -topOffset);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return content;
        }

        // ---------------------------------------------------------------- 通用 UI 工具

        private static Font _font;

        private static Font UiFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        /// <summary>内置 UI 精灵;Filled 类型的进度条必须有 sprite 才会裁切。</summary>
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
            return image;
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

        private static Button UiButton(string name, Transform parent, string label)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.2f, 0.32f, 0.45f, 0.9f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = UiText("Label", rect, label, 14, TextAnchor.MiddleCenter, TextColor);
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

        // ---------------------------------------------------------------- 其它工具

        private static Material SlotMaterial()
        {
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Slot_Marker.mat");
        }

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

        private static GameObject LoadPrefab(GameObject prefab)
        {
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
