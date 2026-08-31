using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RainWorldDesktopPet.Audio
{
    public sealed class RainWorldSoundClipChoice
    {
        internal RainWorldSoundClipChoice(string name)
        {
            Name = name;
            MinimumVolume = MaximumVolume = 1.0;
            MinimumPitch = MaximumPitch = 1.0;
            IgnoreEffects = 0.0;
        }

        public readonly string Name;
        public double MinimumVolume { get; internal set; }
        public double MaximumVolume { get; internal set; }
        public double MinimumPitch { get; internal set; }
        public double MaximumPitch { get; internal set; }
        public double IgnoreEffects { get; internal set; }

        internal double ChooseVolume(Random random)
        {
            return Choose(MinimumVolume, MaximumVolume, random);
        }

        internal double ChoosePitch(Random random)
        {
            return Choose(MinimumPitch, MaximumPitch, random);
        }

        internal static double Choose(double minimum, double maximum, Random random)
        {
            if (maximum <= minimum) return minimum;
            return minimum + (maximum - minimum) * random.NextDouble();
        }
    }

    public sealed class RainWorldSoundDefinition
    {
        internal RainWorldSoundDefinition(string id,
            IList<RainWorldSoundClipChoice> clips)
        {
            Id = id;
            Clips = clips;
            MinimumVolume = MaximumVolume = 1.0;
            MinimumPitch = MaximumPitch = 1.0;
            RangeFactor = 1.0;
            DopplerFactor = 1.0;
        }

        public readonly string Id;
        public bool PlayAll { get; internal set; }
        public readonly IList<RainWorldSoundClipChoice> Clips;
        public double MinimumVolume { get; internal set; }
        public double MaximumVolume { get; internal set; }
        public double MinimumPitch { get; internal set; }
        public double MaximumPitch { get; internal set; }
        public double RangeFactor { get; internal set; }
        public double DopplerFactor { get; internal set; }
        public double IgnoreEffects { get; internal set; }
        public double SilentChance { get; internal set; }

        internal double ChooseVolume(Random random)
        {
            return RainWorldSoundClipChoice.Choose(
                MinimumVolume, MaximumVolume, random);
        }

        internal double ChoosePitch(Random random)
        {
            return RainWorldSoundClipChoice.Choose(
                MinimumPitch, MaximumPitch, random);
        }
    }

    public sealed class RainWorldSoundCatalog
    {
        private readonly Dictionary<string, RainWorldSoundDefinition> definitions =
            new Dictionary<string, RainWorldSoundDefinition>(StringComparer.OrdinalIgnoreCase);

        private RainWorldSoundCatalog()
        {
            MasterVolume = 1.0;
            VolumeExponent = 1.0;
        }

        public int Count { get { return definitions.Count; } }
        public double MasterVolume { get; private set; }
        public double VolumeExponent { get; private set; }

        public static RainWorldSoundCatalog Load(string path)
        {
            return Load(path, null);
        }

        public static RainWorldSoundCatalog Load(string path,
            ISet<string> permittedSoundIds)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException("path");
            RainWorldSoundCatalog catalog = new RainWorldSoundCatalog();
            using (StreamReader reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    catalog.ParseLine(line, permittedSoundIds);
            }
            return catalog;
        }

        public static RainWorldSoundCatalog Parse(string text)
        {
            return Parse(text, null);
        }

        public static RainWorldSoundCatalog Parse(string text,
            ISet<string> permittedSoundIds)
        {
            RainWorldSoundCatalog catalog = new RainWorldSoundCatalog();
            using (StringReader reader = new StringReader(text ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    catalog.ParseLine(line, permittedSoundIds);
            }
            return catalog;
        }

        public bool TryGet(string id, out RainWorldSoundDefinition definition)
        {
            return definitions.TryGetValue(id ?? string.Empty, out definition);
        }

        public ISet<string> ReferencedClipNames()
        {
            HashSet<string> result = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (RainWorldSoundDefinition definition in definitions.Values)
                for (int i = 0; i < definition.Clips.Count; i++)
                    result.Add(definition.Clips[i].Name);
            return result;
        }

        private void ParseLine(string line, ISet<string> permittedSoundIds)
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment);
            int colon = line.IndexOf(':');
            if (colon <= 0) return;

            string left = line.Substring(0, colon).Trim();
            string right = line.Substring(colon + 1).Trim();
            double globalValue;
            if (left.Equals("Volume", StringComparison.OrdinalIgnoreCase))
            {
                if (TryDouble(right, out globalValue)) MasterVolume = globalValue;
                return;
            }
            if (left.Equals("Volume exponent", StringComparison.OrdinalIgnoreCase))
            {
                if (TryDouble(right, out globalValue)) VolumeExponent = globalValue;
                return;
            }
            if (left.StartsWith("[ADD]", StringComparison.OrdinalIgnoreCase))
                left = left.Substring(5).TrimStart();
            if (right.Length == 0) return;

            string[] leftParts = left.Split('/');
            string id = leftParts[0].Trim();
            if (id.Length == 0 ||
                (permittedSoundIds != null && !permittedSoundIds.Contains(id))) return;
            List<RainWorldSoundClipChoice> choices = new List<RainWorldSoundClipChoice>();
            string[] entries = right.Split(',');
            for (int i = 0; i < entries.Length; i++) ParseChoice(entries[i], choices);
            if (choices.Count > 0)
            {
                RainWorldSoundDefinition definition = new RainWorldSoundDefinition(
                    id, choices.AsReadOnly());
                for (int i = 1; i < leftParts.Length; i++)
                    ApplyDefinitionParameter(definition, leftParts[i].Trim());
                definitions[id] = definition;
            }
        }

        private static void ParseChoice(string entry,
            List<RainWorldSoundClipChoice> choices)
        {
            string[] fields = entry.Split('/');
            RainWorldSoundClipChoice current = null;
            for (int i = 0; i < fields.Length; i++)
            {
                string field = fields[i].Trim();
                if (field.Length == 0) continue;
                int equals = field.IndexOf('=');
                if (equals < 0)
                {
                    current = new RainWorldSoundClipChoice(field);
                    choices.Add(current);
                    continue;
                }
                if (current == null) continue;
                string key = field.Substring(0, equals).Trim();
                double value;
                if (!TryDouble(field.Substring(equals + 1).Trim(), out value)) continue;
                ApplyParameter(current, key, value);
            }
        }

        private static void ApplyDefinitionParameter(
            RainWorldSoundDefinition definition, string field)
        {
            if (field.Equals("PLAYALL", StringComparison.OrdinalIgnoreCase))
            {
                definition.PlayAll = true;
                return;
            }
            int equals = field.IndexOf('=');
            if (equals <= 0) return;
            string key = field.Substring(0, equals).Trim();
            double value;
            if (!TryDouble(field.Substring(equals + 1).Trim(), out value)) return;
            if (key.Equals("vol", StringComparison.OrdinalIgnoreCase))
                definition.MinimumVolume = definition.MaximumVolume = value;
            else if (key.Equals("minVol", StringComparison.OrdinalIgnoreCase))
                definition.MinimumVolume = value;
            else if (key.Equals("maxVol", StringComparison.OrdinalIgnoreCase))
                definition.MaximumVolume = value;
            else if (key.Equals("pitch", StringComparison.OrdinalIgnoreCase))
                definition.MinimumPitch = definition.MaximumPitch = value;
            else if (key.Equals("minPitch", StringComparison.OrdinalIgnoreCase))
                definition.MinimumPitch = value;
            else if (key.Equals("maxPitch", StringComparison.OrdinalIgnoreCase))
                definition.MaximumPitch = value;
            else if (key.Equals("rangeFac", StringComparison.OrdinalIgnoreCase))
                definition.RangeFactor = value;
            else if (key.Equals("dopplerFac", StringComparison.OrdinalIgnoreCase))
                definition.DopplerFactor = value;
            else if (key.Equals("ignoreEffects", StringComparison.OrdinalIgnoreCase))
                definition.IgnoreEffects = value;
            else if (key.Equals("silentChance", StringComparison.OrdinalIgnoreCase))
                definition.SilentChance = value;
        }

        private static void ApplyParameter(RainWorldSoundClipChoice choice,
            string key, double value)
        {
            if (key.Equals("vol", StringComparison.OrdinalIgnoreCase))
                choice.MinimumVolume = choice.MaximumVolume = value;
            else if (key.Equals("minVol", StringComparison.OrdinalIgnoreCase))
                choice.MinimumVolume = value;
            else if (key.Equals("maxVol", StringComparison.OrdinalIgnoreCase))
                choice.MaximumVolume = value;
            else if (key.Equals("pitch", StringComparison.OrdinalIgnoreCase))
                choice.MinimumPitch = choice.MaximumPitch = value;
            else if (key.Equals("minPitch", StringComparison.OrdinalIgnoreCase))
                choice.MinimumPitch = value;
            else if (key.Equals("maxPitch", StringComparison.OrdinalIgnoreCase))
                choice.MaximumPitch = value;
            else if (key.Equals("ignoreEffects", StringComparison.OrdinalIgnoreCase))
                choice.IgnoreEffects = value;
        }

        private static bool TryDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }
    }
}
