using System;
using UnityEngine;

namespace Sv.Columns.Entities
{
    static class ColumnsPackNames
    {
        static TextAsset? source;
        static string? text;
        static string[] templates = Array.Empty<string>();
        static string[] lanes = Array.Empty<string>();
        static string error = string.Empty;

        public static string Error => error;

        public static bool Read(ColumnsEntityAuthoring? authoring, out string[] templateNames, out string[] laneNames)
        {
            templateNames = Array.Empty<string>();
            laneNames = Array.Empty<string>();
            var pack = authoring != null && authoring.World != null ? authoring.World.Pack : null;
            if (pack == null)
            {
                error = "wire a ColumnsWorldAuthoring that has a pack to pick names";
                return false;
            }

            var json = pack.text;
            if (!ReferenceEquals(pack, source) || json != text)
            {
                source = pack;
                text = json;
                try
                {
                    using var compiled = PackJson.Read(json).Compile();
                    templates = new string[compiled.TemplateNames.Count];
                    for (var i = 0; i < templates.Length; i++)
                    {
                        templates[i] = compiled.TemplateNames[i];
                    }

                    var count = 0;
                    for (var i = 0; i < compiled.LaneNames.Count; i++)
                    {
                        count += compiled.LaneNames[i][0] == '@' ? 0 : 1;
                    }

                    lanes = new string[count];
                    count = 0;
                    for (var i = 0; i < compiled.LaneNames.Count; i++)
                    {
                        if (compiled.LaneNames[i][0] != '@')
                        {
                            lanes[count++] = compiled.LaneNames[i];
                        }
                    }

                    error = string.Empty;
                }
                catch (Exception failure)
                {
                    templates = Array.Empty<string>();
                    lanes = Array.Empty<string>();
                    error = "the pack does not compile: " + failure.Message;
                }
            }

            templateNames = templates;
            laneNames = lanes;
            return error.Length == 0;
        }
    }
}
