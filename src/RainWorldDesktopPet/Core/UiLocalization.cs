using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace RainWorldDesktopPet.Core
{
    public enum UiLanguage
    {
        Korean,
        English
    }

    public static class UiLocalization
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "language.txt");
        private static bool loaded;
        private static UiLanguage current;

        public static UiLanguage Current
        {
            get
            {
                EnsureLoaded();
                return current;
            }
        }

        public static string Text(string korean, string english)
        {
            return Current == UiLanguage.Korean
                ? NormalizeKoreanText(korean)
                : NormalizeEnglishText(english);
        }

        private static string NormalizeKoreanText(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            switch (value)
            {
                case "Survivor — 생존자": return "생존자";
                case "Monk — 수도승": return "수도승";
                case "Hunter — 사냥꾼": return "사냥꾼";
                case "Gourmand — 대식가": return "대식가";
                case "Artificer — 기술병": return "기술병";
                case "SpearMaster — 창술가": return "창술가";
                case "Rivulet — 물살이": return "물살이";
                case "Saint — 성자": return "성자";
                case "캐릭터와 능력": return "캐릭터";
                default: return value;
            }
        }

        private static string NormalizeEnglishText(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            switch (value)
            {
                case "Character and Ability": return "Character";
                default: return value;
            }
        }

        public static void SetLanguage(UiLanguage language)
        {
            current = language;
            loaded = true;
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(SettingsPath,
                    language == UiLanguage.Korean ? "ko" : "en", Encoding.UTF8);
            }
            catch (Exception)
            {
                // A read-only settings directory must not prevent the app from running.
            }

            RefreshOpenTrayMenus(language);
        }

        private static void RefreshOpenTrayMenus(UiLanguage language)
        {
            try
            {
                for (int formIndex = 0; formIndex < Application.OpenForms.Count; formIndex++)
                {
                    Form form = Application.OpenForms[formIndex];
                    if (form == null || form.IsDisposed) continue;

                    FieldInfo[] fields = form.GetType().GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                    {
                        if (!typeof(NotifyIcon).IsAssignableFrom(fields[fieldIndex].FieldType))
                            continue;
                        NotifyIcon icon = fields[fieldIndex].GetValue(form) as NotifyIcon;
                        if (icon == null || icon.ContextMenuStrip == null) continue;
                        TranslateItems(icon.ContextMenuStrip.Items, language);
                    }
                }
            }
            catch (Exception)
            {
                // Localization must remain non-fatal. Saved language still applies next launch.
            }
        }

        private static void TranslateItems(ToolStripItemCollection items, UiLanguage language)
        {
            if (items == null) return;
            for (int index = 0; index < items.Count; index++)
            {
                ToolStripItem item = items[index];
                if (item == null) continue;
                item.Text = TranslateTrayText(item.Text, language);

                ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
                if (dropDown != null)
                    TranslateItems(dropDown.DropDownItems, language);
            }
        }

        private static string TranslateTrayText(string value, UiLanguage language)
        {
            if (string.IsNullOrEmpty(value)) return value;

            if (language == UiLanguage.English)
            {
                switch (value)
                {
                    case "설정 열기": return "Open Settings";
                    case "디버그 오버레이": return "Debug Overlay";
                    case "모든 슬러그캣 일시 정지": return "Pause All Slugcats";
                    case "렌더링 재시도": return "Retry Rendering";
                    case "스킨 편집기 (실험적)": return "Skin Editor (Experimental)";
                    case "종료": return "Exit";
                    case "캐릭터와 능력": return "Character";
                    case "캐릭터": return "Character";
                    case "슬러그캣": return "Slugcats";
                    case "Workshop 모드 새로 고침": return "Refresh Workshop Mods";
                    case "슬러그캣 추가": return "Add Slugcat";
                    case "다음 슬러그캣 선택": return "Select Next Slugcat";
                    case "선택한 슬러그캣 삭제": return "Remove Selected Slugcat";
                    case "먹이 주기": return "Feed";
                    case "푸른 열매 주기": return "Give Blue Fruit";
                    case "알벌레 알 주기": return "Give Eggbug Egg";
                    case "슬러그캣 포만감": return "Slugcat Fullness";
                    case "먹이 치우기": return "Clear Food";
                    case "생존자": return "Survivor";
                    case "수도승": return "Monk";
                    case "사냥꾼": return "Hunter";
                    case "대식가": return "Gourmand";
                    case "기술병": return "Artificer";
                    case "창술가": return "SpearMaster";
                    case "물살이": return "Rivulet";
                    case "성자": return "Saint";
                }

                if (value.StartsWith("슬러그캣 (", StringComparison.Ordinal))
                    return "Slugcats" + value.Substring("슬러그캣".Length);
                if (value.StartsWith("슬러그캣 ", StringComparison.Ordinal))
                {
                    string translated = "Slugcat " + value.Substring("슬러그캣 ".Length);
                    translated = translated.Replace(" · 포만감 ", " · Fullness ");
                    return TranslateSlugcatNames(translated, language);
                }
                return TranslateSlugcatNames(
                    value.Replace(" · 포만감 ", " · Fullness "), language);
            }

            switch (value)
            {
                case "Open Settings": return "설정 열기";
                case "Debug Overlay": return "디버그 오버레이";
                case "Pause All Slugcats": return "모든 슬러그캣 일시 정지";
                case "Retry Rendering": return "렌더링 재시도";
                case "Skin Editor (Experimental)": return "스킨 편집기 (실험적)";
                case "Exit": return "종료";
                case "Character and Ability": return "캐릭터";
                case "Character": return "캐릭터";
                case "Refresh Workshop Mods": return "Workshop 모드 새로 고침";
                case "Slugcats": return "슬러그캣";
                case "Add Slugcat": return "슬러그캣 추가";
                case "Select Next Slugcat": return "다음 슬러그캣 선택";
                case "Remove Selected Slugcat": return "선택한 슬러그캣 삭제";
                case "Feed": return "먹이 주기";
                case "Give Blue Fruit": return "푸른 열매 주기";
                case "Give Eggbug Egg": return "알벌레 알 주기";
                case "Slugcat Fullness": return "슬러그캣 포만감";
                case "Clear Food": return "먹이 치우기";
                case "Survivor": return "생존자";
                case "Monk": return "수도승";
                case "Hunter": return "사냥꾼";
                case "Gourmand": return "대식가";
                case "Artificer": return "기술병";
                case "SpearMaster": return "창술가";
                case "Rivulet": return "물살이";
                case "Saint": return "성자";
            }

            if (value.StartsWith("Slugcats (", StringComparison.Ordinal))
                return "슬러그캣" + value.Substring("Slugcats".Length);
            if (value.StartsWith("Slugcat ", StringComparison.Ordinal))
            {
                string translated = "슬러그캣 " + value.Substring("Slugcat ".Length);
                translated = translated.Replace(" · Fullness ", " · 포만감 ");
                return TranslateSlugcatNames(translated, language);
            }
            return TranslateSlugcatNames(
                value.Replace(" · Fullness ", " · 포만감 "), language);
        }

        private static string TranslateSlugcatNames(string value, UiLanguage language)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (language == UiLanguage.English)
            {
                return value
                    .Replace("생존자", "Survivor")
                    .Replace("수도승", "Monk")
                    .Replace("사냥꾼", "Hunter")
                    .Replace("대식가", "Gourmand")
                    .Replace("기술병", "Artificer")
                    .Replace("창술가", "SpearMaster")
                    .Replace("물살이", "Rivulet")
                    .Replace("성자", "Saint");
            }

            return value
                .Replace("SpearMaster", "창술가")
                .Replace("Survivor", "생존자")
                .Replace("Monk", "수도승")
                .Replace("Hunter", "사냥꾼")
                .Replace("Gourmand", "대식가")
                .Replace("Artificer", "기술병")
                .Replace("Rivulet", "물살이")
                .Replace("Saint", "성자");
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string value = File.ReadAllText(SettingsPath).Trim();
                    if (string.Equals(value, "ko", StringComparison.OrdinalIgnoreCase))
                    {
                        current = UiLanguage.Korean;
                        return;
                    }
                    if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
                    {
                        current = UiLanguage.English;
                        return;
                    }
                }
            }
            catch (Exception)
            {
            }

            current = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                "ko", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.Korean : UiLanguage.English;
        }
    }
}
