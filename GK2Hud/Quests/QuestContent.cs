using System.Collections.Generic;
using System.Linq;
using GK2Hud.Calendar;
using GK2Hud.Tooltips;
using LazyBearTechnology;
using TMPro;
using UnityEngine.UI;

namespace GK2Hud.Quests
{
    /// <summary>Conteúdo dos tooltips da árvore de quests no <c>UITooltip</c> nativo: o nó (quest) e a ficha do NPC.</summary>
    internal static class QuestContent
    {
        private const int MaxEarlier = 4;
        private const int MaxNotes = 5;

        /// <summary>Um nó da árvore: título, NPC e onde ele está, estado com a data, texto atual e os passos anteriores do nó.</summary>
        public static List<LazyWidgetDataBase> Node(QuestData q, bool hint)
        {
            Styles s = Styles.Read();
            var list = new List<LazyWidgetDataBase>();
            if (s == null || q?.Definition == null) return list;
            string title = QuestModel.Title(q);
            if (title != null) list.Add(Text(title, s.Header, TextAlignmentOptions.Center));
            string npcId = q.Definition.wgoNpcId;
            string npc = QuestModel.NpcName(npcId);
            if (npc != null)
            {
                string where = QuestModel.Where(npcId);
                list.Add(Text(Links.Wrap(Links.Npc, npcId, npc) + (where != null ? " · " + where : ""), s.Small, TextAlignmentOptions.Center));
            }
            list.Add(Text(Status(q, s), s.Small, TextAlignmentOptions.Center));
            list.Add(new UITooltipSeparatorWidgetData());
            string text = QuestModel.Text(q);
            if (text != null) list.Add(Text(text, s.Description, TextAlignmentOptions.Center));
            string needs = Needs(q, s);
            if (needs != null) list.Add(Text(needs, s.Small, TextAlignmentOptions.Center));

            List<QuestData> earlier = QuestModel.Chain(q).Where(o => o != q).ToList();
            var lines = new List<string>();
            var seen = new HashSet<string> { text ?? "" };
            for (int i = earlier.Count - 1; i >= 0; i--)
            {
                string t = QuestModel.Text(earlier[i]);
                if (t == null || !seen.Add(t)) continue;
                lines.Add(DayPrefix(earlier[i], s) + t);
            }
            if (lines.Count > 0)
            {
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(Text(QuestL10n.T("earlier"), s.SmallSelected, TextAlignmentOptions.Center));
                foreach (string l in lines.Take(MaxEarlier)) list.Add(Text(l, s.Small, TextAlignmentOptions.Center));
                if (lines.Count > MaxEarlier) list.Add(Text(QuestL10n.T("more", lines.Count - MaxEarlier), s.Small, TextAlignmentOptions.Center));
            }
            if (hint && npc != null)
            {
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(Text(QuestL10n.T("about", npc), s.Small, TextAlignmentOptions.Center));
            }
            Names.Scrub(list);
            return list;
        }

        /// <summary>Ficha do NPC: onde está agora, reputação, o que espera do jogador e o que as quests dele já contaram.</summary>
        public static List<LazyWidgetDataBase> Npc(string npcId)
        {
            Styles s = Styles.Read();
            var list = new List<LazyWidgetDataBase>();
            string name = QuestModel.NpcName(npcId);
            if (s == null || name == null) return list;
            list.Add(Text(name, s.Header, TextAlignmentOptions.Center));
            string where = QuestModel.Where(npcId);
            list.Add(Text(where != null ? s.SmallSelected.ApplyStyleToString(QuestL10n.T("now_at") + ": ") + where : QuestL10n.T("whereabouts"), s.Small, TextAlignmentOptions.Center));
            string rep = Reputation(npcId);
            if (rep != null) list.Add(Text(rep, s.Small, TextAlignmentOptions.Center));

            List<QuestData> quests = QuestModel.OfNpc(npcId);
            List<QuestData> open = quests.Where(q => q.status == QuestStatus.InProgress).ToList();
            if (open.Count > 0)
            {
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(Text(QuestL10n.T("wants"), s.SmallSelected, TextAlignmentOptions.Center));
                var seen = new HashSet<string>();
                foreach (QuestData q in open)
                {
                    string t = QuestModel.Text(q);
                    if (t == null || !seen.Add(t)) continue;
                    string title = QuestModel.Title(q);
                    if (title != null) list.Add(Text(Links.Wrap(Links.Quest, q.id, title), s.Description, TextAlignmentOptions.Center));
                    list.Add(Text(t, s.Small, TextAlignmentOptions.Center));
                }
            }
            var notes = new List<string>();
            var seenNotes = new HashSet<string>();
            foreach (QuestData q in quests.Where(q => q.status == QuestStatus.Completed))
            {
                string t = Names.Text("quest_closed_" + q.id + "_d");
                if (t == null || !seenNotes.Add(t)) continue;
                notes.Add(DayPrefix(q, s) + t);
            }
            if (notes.Count > 0)
            {
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(Text(QuestL10n.T("notes"), s.SmallSelected, TextAlignmentOptions.Center));
                foreach (string n in notes.Take(MaxNotes)) list.Add(Text(n, s.Small, TextAlignmentOptions.Center));
                if (notes.Count > MaxNotes) list.Add(Text(QuestL10n.T("more", notes.Count - MaxNotes), s.Small, TextAlignmentOptions.Center));
            }
            Names.Scrub(list);
            return list;
        }

        private static string Status(QuestData q, Styles s)
        {
            bool done = q.status == QuestStatus.Completed;
            string state = QuestL10n.T(done ? "done" : q.status == QuestStatus.InProgress ? "in_progress" : "available");
            QuestModel.Moment m = QuestModel.When(q, done);
            string when = null;
            if (m.Known) when = done ? QuestL10n.T("on_day", m.Day, (m.Approx ? "~" : "") + CalendarModel.Clock(m.Tod)) : QuestL10n.T("since_day", m.Day);
            else if (m.BeforeJournal) when = QuestL10n.T("before_journal", m.JournalStart);
            return s.SmallSelected.ApplyStyleToString(state) + (when != null ? " · " + when : "");
        }

        private static string DayPrefix(QuestData q, Styles s)
        {
            QuestModel.Moment m = QuestModel.When(q, true);
            return m.Known ? s.SmallSelected.ApplyStyleToString(QuestL10n.T("day_short", m.Day) + ": ") : "";
        }

        /// <summary>O que a conversa de conclusão cobra (itens), com quanto o jogador tem no inventário.</summary>
        private static string Needs(QuestData q, Styles s)
        {
            if (q.status != QuestStatus.InProgress || q.Definition.finishCheck?.phraseReqs == null || MainGame.PlayerData == null) return null;
            var parts = new List<string>();
            foreach (QuestPhraseRequirement r in q.Definition.finishCheck.phraseReqs)
            {
                if (r.entity != QuestPhraseRequirement.Entity.Item || r.itemCount == null) continue;
                ItemDef def = GameBalance.Me.GetDataOrNull<ItemDef>(r.itemCount.itemId);
                string name = def?.GetHeader();
                if (string.IsNullOrEmpty(name) || Names.IsDevId(name)) continue;
                int have = MainGame.PlayerData.Inventory.Data.GetTotalCountInInventory(r.itemCount.itemId);
                parts.Add(Links.Wrap(Links.Item, def.id, name) + " ×" + r.itemCount.count + " (" + QuestL10n.T("have", have) + ")");
            }
            return parts.Count == 0 ? null : s.SmallSelected.ApplyStyleToString(QuestL10n.T("needs") + ": ") + string.Join(", ", parts);
        }

        /// <summary>Reputação como o widget nativo de NPC mostra (ícone do recurso e valor de 0 a 100); lida crua.</summary>
        private static string Reputation(string npcId)
        {
            WGODef def = GameBalance.Me.GetDataOrNull<WGODef>(npcId);
            if (def == null || string.IsNullOrEmpty(def.repResName) || MainGame.PlayerData == null) return null;
            GameResIconConfig cfg = GameResDisplayConfig.GetConfigForRes(def.repResName, GameResIconType.Common);
            string icon = (cfg == null ? "icon_smile02" : cfg.iconName).FontIcon();
            return icon + " " + MainGame.PlayerData.GetNPCRep(def.repResName) + "/100";
        }

        private static UITooltipTextWidgetData Text(string text, TextStyle style, TextAlignmentOptions align) => new UITooltipTextWidgetData(text, align, style);
    }
}
