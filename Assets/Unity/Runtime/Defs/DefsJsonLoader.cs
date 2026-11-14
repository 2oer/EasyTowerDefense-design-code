using System.IO;
using Game.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace Game.UnityBridge
{
    /// <summary>
    /// 从 StreamingAssets/Defs 读取权威配置表。
    /// 依赖 Unity 官方包 com.unity.nuget.newtonsoft-json(见 Design/Unity接入说明.md)。
    /// 核心层仍保持纯 C#:JSON 只在表现 / 宿主层解析。
    /// </summary>
    public static class DefsJsonLoader
    {
        public static ModelDatabase LoadFromStreamingAssets(string subFolder = "Defs")
        {
            string directory = Path.Combine(Application.streamingAssetsPath, subFolder);
            return Load(directory);
        }

        public static ModelDatabase Load(string directory)
        {
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };
            settings.Converters.Add(new StringEnumConverter());

            var db = new ModelDatabase();
            db.Towers.AddRange(ReadList<TowerDef>(directory, "towers.json", settings));
            db.Enemies.AddRange(ReadList<EnemyDef>(directory, "enemies.json", settings));
            db.Difficulties.AddRange(ReadList<DifficultyDef>(directory, "difficulties.json", settings));
            db.Maps.AddRange(ReadList<MapDef>(directory, "maps.json", settings));
            db.Upgrades.AddRange(ReadList<UpgradeNodeDef>(directory, "upgrades.json", settings));
            db.Waves = Read<WaveRules>(directory, "waves.json", settings);
            db.Effects = Read<StatusRules>(directory, "effects.json", settings);

            // 启动期硬校验:表配置有误直接抛错,不带病运行。
            ModelValidator.Validate(db);
            return db;
        }

        private static T Read<T>(string directory, string fileName, JsonSerializerSettings settings)
            where T : class
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) throw new FileNotFoundException("缺少配置表:" + path);

            T value = JsonConvert.DeserializeObject<T>(File.ReadAllText(path), settings);
            if (value == null) throw new InvalidDataException("配置表解析为空:" + path);
            return value;
        }

        private static System.Collections.Generic.List<T> ReadList<T>(
            string directory, string fileName, JsonSerializerSettings settings)
        {
            System.Collections.Generic.List<T> list =
                JsonConvert.DeserializeObject<System.Collections.Generic.List<T>>(
                    File.ReadAllText(Path.Combine(directory, fileName)), settings);
            if (list == null) throw new InvalidDataException("配置表解析为空:" + fileName);
            return list;
        }
    }
}
