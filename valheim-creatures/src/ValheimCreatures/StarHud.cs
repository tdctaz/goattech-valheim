using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimCreatures
{
    internal static class StarHud
    {
        private const string RowName = "ValheimCreatures_Stars";
        private const string BossStarName = "ValheimCreatures_BossStar";
        private const float BossStarGap = 8f;
        private const float OutlineLuminance = 0.25f;

        private sealed class Row
        {
            internal RectTransform Parent;
            internal GameObject Template;
            internal Vector2 Center;
            internal Vector2 Step;
            internal readonly List<GameObject> Units = new List<GameObject>();
            internal readonly List<Color> Original = new List<Color>();
            internal readonly List<Color> TemplateOriginal = new List<Color>();
            internal int Shown = -1;
            internal StarColor Tinted = (StarColor)(-1);
        }

        private sealed class BossStars
        {
            internal RectTransform Bar;
            internal Tinted Left;
            internal Tinted Right;
        }

        private sealed class Tinted
        {
            internal GameObject Unit;
            internal readonly List<Graphic> Graphics = new List<Graphic>();
            internal readonly List<Color> Original = new List<Color>();
            internal StarColor Color = (StarColor)(-1);
        }

        private static readonly Dictionary<GameObject, Row> Rows = new Dictionary<GameObject, Row>();
        private static readonly Dictionary<GameObject, BossStars> Bosses = new Dictionary<GameObject, BossStars>();
        private static readonly List<GameObject> Dead = new List<GameObject>();
#if DEBUG_TOOLS
        private static readonly Dictionary<Character, string> Logged = new Dictionary<Character, string>();
        private static readonly List<Character> DeadCharacters = new List<Character>();
#endif

        internal static void Update(EnemyHud hud)
        {
            foreach (KeyValuePair<Character, EnemyHud.HudData> entry in hud.m_huds)
            {
                EnemyHud.HudData data = entry.Value;
                Character character = data.m_character;
                if (data.m_gui == null || character == null || character.IsPlayer() || data.m_isMount)
                {
                    continue;
                }

                CreatureTraits traits = CreatureTraits.Of(character);
                StarColor color = traits != null ? traits.Color : StarColor.None;

                if (character.IsBoss())
                {
                    UpdateBoss(hud, data, color, traits != null ? traits.SecondColor : StarColor.None);
                    continue;
                }

                int stars = Mathf.Max(0, character.GetLevel() - 1);
                bool custom = stars > 2 || (stars > 0 && color != StarColor.None);
#if DEBUG_TOOLS
                if (ModConfig.LogColors.Value)
                {
                    string state = $"stars {stars} traits {(traits != null)} color {color} custom {custom}";
                    if (!Logged.TryGetValue(character, out string last) || last != state)
                    {
                        Logged[character] = state;
                        Plugin.Log.LogInfo($"Colors: HUD {Utils.GetPrefabName(character.gameObject)} {state}.");
                    }
                }
#endif

                if (!custom)
                {
                    Hide(data.m_gui);
                    continue;
                }

                if (data.m_level2 != null)
                {
                    data.m_level2.gameObject.SetActive(false);
                }

                if (data.m_level3 != null)
                {
                    data.m_level3.gameObject.SetActive(false);
                }

                Row row = RowFor(data);
                if (row != null)
                {
                    Show(row, stars, color);
                }
            }

            Prune();
        }

        private static void UpdateBoss(EnemyHud hud, EnemyHud.HudData data, StarColor first, StarColor second)
        {
            BossStars stars = BossStarsFor(hud, data);
            if (stars == null)
            {
                return;
            }

            StarColor left = second != StarColor.None ? first : StarColor.None;
            StarColor right = second != StarColor.None ? second : first;
            Rect bar = stars.Bar.rect;
            Place(stars.Left, left, new Vector2(bar.xMin - BossStarGap - Width(stars.Left) * 0.5f, bar.center.y));
            Place(stars.Right, right, new Vector2(bar.xMax + BossStarGap + Width(stars.Right) * 0.5f, bar.center.y));
        }

        private static float Width(Tinted star)
        {
            return star.Unit.transform is RectTransform rect ? rect.rect.width : 20f;
        }

        private static void Place(Tinted star, StarColor color, Vector2 position)
        {
            bool show = color != StarColor.None;
            if (star.Unit.activeSelf != show)
            {
                star.Unit.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            star.Unit.transform.localPosition = position;
            if (star.Color != color)
            {
                for (int i = 0; i < star.Graphics.Count; i++)
                {
                    star.Graphics[i].color = TintOf(star.Original[i], color);
                }

                star.Color = color;
            }
        }

        private static BossStars BossStarsFor(EnemyHud hud, EnemyHud.HudData data)
        {
            if (Bosses.TryGetValue(data.m_gui, out BossStars stars))
            {
                return stars;
            }

            Transform level2 = hud.m_baseHud != null ? hud.m_baseHud.transform.Find("level_2") : null;
            Transform template = level2 != null ? level2.Find("star") ?? level2 : null;
            RectTransform bar = data.m_gui.transform.Find("Health") as RectTransform;
            if (template == null || bar == null)
            {
                return null;
            }

            stars = new BossStars
            {
                Bar = bar,
                Left = MakeBossStar(template, bar),
                Right = MakeBossStar(template, bar),
            };

            Bosses[data.m_gui] = stars;
            return stars;
        }

        private static Tinted MakeBossStar(Transform template, RectTransform bar)
        {
            GameObject unit = Object.Instantiate(template.gameObject, bar, false);
            unit.name = BossStarName;
            unit.SetActive(false);
            if (unit.transform is RectTransform rect)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
            }

            Tinted star = new Tinted { Unit = unit };
            foreach (Graphic graphic in unit.GetComponentsInChildren<Graphic>(true))
            {
                star.Graphics.Add(graphic);
                star.Original.Add(graphic.color);
            }

            return star;
        }

        private static Row RowFor(EnemyHud.HudData data)
        {
            if (Rows.TryGetValue(data.m_gui, out Row row))
            {
                return row;
            }

            RectTransform level3 = data.m_level3;
            RectTransform level2 = data.m_level2;
            if (level3 != null && StarUnits(level3).Count >= 2)
            {
                GameObject root = Object.Instantiate(level3.gameObject, level3.parent);
                root.name = RowName;
                root.SetActive(true);
                List<RectTransform> units = StarUnits(root.transform as RectTransform);
                row = new Row
                {
                    Parent = root.transform as RectTransform,
                    Template = units[0].gameObject,
                    Step = units[1].anchoredPosition - units[0].anchoredPosition,
                    Center = (units[0].anchoredPosition + units[1].anchoredPosition) * 0.5f,
                };

                foreach (RectTransform unit in units)
                {
                    Adopt(row, unit.gameObject);
                }
            }
            else if (level2 != null)
            {
                row = new Row
                {
                    Parent = level2.parent as RectTransform,
                    Template = level2.gameObject,
                    Step = new Vector2(Mathf.Max(8f, level2.rect.width), 0f),
                    Center = level2.anchoredPosition,
                };
            }
            else
            {
                return null;
            }

            foreach (Graphic g in row.Template.GetComponentsInChildren<Graphic>(true))
            {
                row.TemplateOriginal.Add(g.color);
            }

            Rows[data.m_gui] = row;
            return row;
        }

        private static void Show(Row row, int stars, StarColor color)
        {
            row.Parent.gameObject.SetActive(true);

            while (row.Units.Count < stars)
            {
                GameObject unit = Object.Instantiate(row.Template, row.Parent, false);
                unit.name = RowName + "_" + row.Units.Count;
                unit.SetActive(true);
                row.Units.Add(unit);
                row.Original.AddRange(row.TemplateOriginal);
            }

            if (row.Shown != stars)
            {
                for (int i = 0; i < row.Units.Count; i++)
                {
                    GameObject unit = row.Units[i];
                    unit.SetActive(i < stars);
                    if (i < stars && unit.transform is RectTransform rect)
                    {
                        rect.anchoredPosition = row.Center + row.Step * (i - (stars - 1) * 0.5f);
                    }
                }

                row.Shown = stars;
                row.Tinted = (StarColor)(-1);
            }

            if (row.Tinted != color)
            {
                int graphic = 0;
                foreach (GameObject unit in row.Units)
                {
                    foreach (Graphic g in unit.GetComponentsInChildren<Graphic>(true))
                    {
                        if (graphic < row.Original.Count)
                        {
                            g.color = TintOf(row.Original[graphic], color);
                        }

                        graphic++;
                    }
                }

                row.Tinted = color;
            }
        }

        private static Color TintOf(Color original, StarColor color)
        {
            if (color == StarColor.None)
            {
                return original;
            }

            float luminance = 0.299f * original.r + 0.587f * original.g + 0.114f * original.b;
            if (luminance < OutlineLuminance)
            {
                return original;
            }

            Color tint = StarColors.Tint(color);
            tint.a = original.a;
            return tint;
        }

        private static void Adopt(Row row, GameObject unit)
        {
            unit.SetActive(true);
            row.Units.Add(unit);
            foreach (Graphic g in unit.GetComponentsInChildren<Graphic>(true))
            {
                row.Original.Add(g.color);
            }
        }

        private static List<RectTransform> StarUnits(RectTransform parent)
        {
            List<RectTransform> units = new List<RectTransform>();
            foreach (Transform child in parent)
            {
                if (child is RectTransform rect && child.GetComponentInChildren<Graphic>(true) != null)
                {
                    units.Add(rect);
                }
            }

            return units;
        }

        private static void Hide(GameObject gui)
        {
            if (Rows.TryGetValue(gui, out Row row) && row.Shown != 0)
            {
                foreach (GameObject unit in row.Units)
                {
                    unit.SetActive(false);
                }

                row.Shown = 0;
            }
        }

        private static void Prune()
        {
            Dead.Clear();
            foreach (GameObject gui in Rows.Keys)
            {
                if (gui == null)
                {
                    Dead.Add(gui);
                }
            }

            foreach (GameObject gui in Bosses.Keys)
            {
                if (gui == null)
                {
                    Dead.Add(gui);
                }
            }

            foreach (GameObject gui in Dead)
            {
                Rows.Remove(gui);
                Bosses.Remove(gui);
            }
#if DEBUG_TOOLS

            if (Logged.Count == 0)
            {
                return;
            }

            DeadCharacters.Clear();
            foreach (Character character in Logged.Keys)
            {
                if (character == null)
                {
                    DeadCharacters.Add(character);
                }
            }

            foreach (Character character in DeadCharacters)
            {
                Logged.Remove(character);
            }
#endif
        }
    }
}
