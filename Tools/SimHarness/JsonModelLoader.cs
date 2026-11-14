using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Model;

namespace SimHarness
{
    /// <summary>
    /// JSON 权威表加载。放在 Harness 而不是 Model 里,是为了让 Model 保持纯数据、
    /// 不带 System.Text.Json 依赖,方便后续原样搬进 Unity。
    /// </summary>
    internal static class JsonModelLoader
    {
        public static ModelDatabase Load(string directory)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var db = new ModelDatabase();
            db.Towers.AddRange(Read<List<TowerDef>>(Path.Combine(directory, "towers.json"), options));
            db.Enemies.AddRange(Read<List<EnemyDef>>(Path.Combine(directory, "enemies.json"), options));
            db.Difficulties.AddRange(Read<List<DifficultyDef>>(Path.Combine(directory, "difficulties.json"), options));
            db.Maps.AddRange(Read<List<MapDef>>(Path.Combine(directory, "maps.json"), options));
            db.Upgrades.AddRange(Read<List<UpgradeNodeDef>>(Path.Combine(directory, "upgrades.json"), options));
            db.Waves = Read<WaveRules>(Path.Combine(directory, "waves.json"), options);
            db.Effects = Read<StatusRules>(Path.Combine(directory, "effects.json"), options);
            return db;
        }

        private static T Read<T>(string path, JsonSerializerOptions options) where T : class
        {
            if (!File.Exists(path)) throw new FileNotFoundException("缺少配置表:" + path);
            string json = File.ReadAllText(path);
            T? value = JsonSerializer.Deserialize<T>(json, options);
            if (value == null) throw new InvalidDataException("配置表解析为空:" + path);
            return value;
        }
    }
}
