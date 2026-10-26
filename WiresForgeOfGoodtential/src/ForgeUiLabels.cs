using System;

namespace WiresForgeOfGoodtential
{
    // These are mod-specific captions, so translate them using Valheim's
    // selected language rather than treating CI/CT as untranslated shortcuts.
    // Unknown/community languages use English.
    internal static class ForgeUiLabels
    {
        internal static string GetCycleLabel(int kind)
        {
            return Get(kind == 0 ? 0 : 1);
        }

        internal static string GetIdol() { return Get(2); }
        internal static string GetNoIdol() { return Get(3); }
        internal static string GetConsumeIdol()
        {
            string language = Localization.instance != null
                ? Localization.instance.GetSelectedLanguage()
                : "English";
            switch (language)
            {
                case "German": return "Idol verbraucht";
                case "French": return "idole consommée";
                case "Spanish": return "ídolo consumido";
                case "Italian": return "idolo consumato";
                case "Swedish": return "idol förbrukad";
                case "Norwegian": return "idol brukt";
                case "Danish": return "idol brugt";
                case "Finnish": return "idoli kulutettu";
                case "Dutch": return "idool verbruikt";
                case "Polish": return "idol zużyty";
                case "Russian": return "идол потрачен";
                case "Ukrainian": return "ідол витрачено";
                case "Portuguese_Brazilian":
                case "Portuguese_European": return "ídolo consumido";
                case "Czech": return "idol spotřebován";
                case "Slovak": return "idol spotrebovaný";
                case "Hungarian": return "bálvány elhasználva";
                case "Turkish": return "idol tüketildi";
                case "Chinese": return "神像已消耗";
                case "Chinese_Trad": return "神像已消耗";
                case "Japanese": return "偶像消費";
                case "Korean": return "우상 소모";
                case "Greek": return "είδωλο καταναλώθηκε";
                case "Romanian": return "idol consumat";
                case "Thai": return "ใช้รูปเคารพแล้ว";
                case "Vietnamese": return "đã dùng thần tượng";
                case "Indonesian": return "idola digunakan";
                default: return "idol consumed";
            }
        }
        internal static string GetTrophyConsumed() { return Get(5); }

        private static string Get(int key)
        {
            string language = Localization.instance != null
                ? Localization.instance.GetSelectedLanguage()
                : "English";

            switch (language)
            {
                case "German":
                    return Choose(key, "Idole", "Trophäen", "Idol",
                        "Kein Idol verfügbar", "Idol behalten",
                        "Trophäe verbraucht");
                case "French":
                    return Choose(key, "Idoles", "Trophées", "Idole",
                        "Aucune idole disponible", "conserver l'idole",
                        "trophée consommé");
                case "Spanish":
                    return Choose(key, "Ídolos", "Trofeos", "Ídolo",
                        "No hay ídolos disponibles", "conservar ídolo",
                        "trofeo consumido");
                case "Italian":
                    return Choose(key, "Idoli", "Trofei", "Idolo",
                        "Nessun idolo disponibile", "conservare idolo",
                        "trofeo consumato");
                case "Swedish":
                    return Choose(key, "Idoler", "Troféer", "Idol",
                        "Ingen idol tillgänglig", "behåll idol",
                        "trofé förbrukad");
                case "Norwegian":
                    return Choose(key, "Idoler", "Trofeer", "Idol",
                        "Ingen idol tilgjengelig", "behold idol",
                        "trofé brukt");
                case "Danish":
                    return Choose(key, "Idoler", "Trofæer", "Idol",
                        "Intet idol tilgængeligt", "behold idol",
                        "trofæ brugt");
                case "Finnish":
                    return Choose(key, "Idolit", "Pokaalit", "Idoli",
                        "Ei idolia saatavilla", "säilytä idoli",
                        "pokaali käytetty");
                case "Dutch":
                    return Choose(key, "Idolen", "Trofeeën", "Idool",
                        "Geen idool beschikbaar", "idool behouden",
                        "trofee verbruikt");
                case "Polish":
                    return Choose(key, "Idole", "Trofea", "Idol",
                        "Brak dostępnego idola", "zachowaj idola",
                        "trofeum zużyte");
                case "Russian":
                    return Choose(key, "Идолы", "Трофеи", "Идол",
                        "Нет доступного идола", "сохранить идол",
                        "трофей потрачен");
                case "Ukrainian":
                    return Choose(key, "Ідоли", "Трофеї", "Ідол",
                        "Немає доступного ідола", "зберегти ідола",
                        "трофей витрачено");
                case "Portuguese_Brazilian":
                case "Portuguese_European":
                    return Choose(key, "Ídolos", "Troféus", "Ídolo",
                        "Nenhum ídolo disponível", "manter ídolo",
                        "troféu consumido");
                case "Czech":
                    return Choose(key, "Idoly", "Trofeje", "Idol",
                        "Žádný idol není k dispozici", "uchovat idol",
                        "trofej spotřebována");
                case "Slovak":
                    return Choose(key, "Idoly", "Trofeje", "Idol",
                        "Žiadny idol nie je dostupný", "zachovať idol",
                        "trofej spotrebovaná");
                case "Hungarian":
                    return Choose(key, "Bálványok", "Trófeák", "Bálvány",
                        "Nincs elérhető bálvány", "bálvány megtartása",
                        "trófea elhasználva");
                case "Turkish":
                    return Choose(key, "İdoller", "Kupalar", "İdol",
                        "İdol bulunmuyor", "idolü koru",
                        "kupa tüketildi");
                case "Chinese":
                    return Choose(key, "神像", "战利品", "神像",
                        "没有可用的神像", "保留神像",
                        "战利品已消耗");
                case "Chinese_Trad":
                    return Choose(key, "神像", "戰利品", "神像",
                        "沒有可用的神像", "保留神像",
                        "戰利品已消耗");
                case "Japanese":
                    return Choose(key, "偶像", "トロフィー", "偶像",
                        "使用可能な偶像がない", "偶像を保持",
                        "トロフィー消費");
                case "Korean":
                    return Choose(key, "우상", "전리품", "우상",
                        "사용 가능한 우상 없음", "우상 보존",
                        "전리품 소모");
                case "Greek":
                    return Choose(key, "Είδωλα", "Τρόπαια", "Είδωλο",
                        "Δεν υπάρχει διαθέσιμο είδωλο", "διατήρηση ειδώλου",
                        "τρόπαιο καταναλώθηκε");
                case "Romanian":
                    return Choose(key, "Idoli", "Trofee", "Idol",
                        "Niciun idol disponibil", "păstrează idolul",
                        "trofeu consumat");
                case "Thai":
                    return Choose(key, "รูปเคารพ", "ถ้วยรางวัล", "รูปเคารพ",
                        "ไม่มีรูปเคารพ", "รักษารูปเคารพ",
                        "ใช้ถ้วยรางวัลแล้ว");
                case "Vietnamese":
                    return Choose(key, "Thần tượng", "Chiến lợi phẩm",
                        "Thần tượng", "Không có thần tượng",
                        "giữ lại thần tượng", "đã dùng chiến lợi phẩm");
                case "Indonesian":
                    return Choose(key, "Idola", "Trofi", "Idola",
                        "Tidak ada idola", "simpan idola",
                        "trofi digunakan");
                default:
                    return Choose(key, "Idols", "Trophies", "Idol",
                        "No idol available", "keep idol",
                        "trophy consumed");
            }
        }

        private static string Choose(int key, string idols,
            string trophies, string idol, string missing,
            string keep, string consumed)
        {
            switch (key)
            {
                case 0: return idols;
                case 1: return trophies;
                case 2: return idol;
                case 3: return missing;
                case 4: return keep;
                default: return consumed;
            }
        }
    }
}
