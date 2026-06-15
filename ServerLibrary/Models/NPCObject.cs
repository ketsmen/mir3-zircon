using Library;
using Library.Network;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using S = Library.Network.ServerPackets;

namespace Server.Models
{
    public class NPCObject : MapObject
    {
        public override ObjectType Race => ObjectType.NPC;

        public NPCInfo NPCInfo;
        public CompiledTextNpcInfo TextInfo;

        public override string Name => TextInfo?.NPCName ?? NPCInfo.NPCName;

        public override bool Blocking => Visible;

        public void NPCCall(PlayerObject ob)
        {
            if (TextInfo != null)
            {
                NPCCall(ob, TextInfo.Script.EntryPageCall);
                return;
            }

            NPCCall(ob, NPCInfo.EntryPage);
        }

        public void NPCCall(PlayerObject ob, NPCPage page)
        {
            while (true)
            {
                if (page == null) return;

                if (!CheckPage(ob, page, out NPCPage failPage))
                {
                    page = failPage;
                    continue;
                }

                DoActions(ob, page);

                if (string.IsNullOrEmpty(page.Say))
                {
                    if (page.SuccessPage != null)
                    {
                        page = page.SuccessPage;
                        continue;
                    }

                    ob.NPC = null;
                    ob.NPCPage = null;
                    ob.NPCTextPage = null;
                    ob.NPCTextBlock = null;
                    ob.NPCTextArguments = null;
                    ob.Enqueue(new S.NPCClose());
                    return;
                }

                var values = GetValues(ob, page);

                ob.NPC = this;
                ob.NPCPage = page;
                ob.NPCTextPage = null;
                ob.NPCTextBlock = null;
                ob.NPCTextArguments = null;

                ob.Enqueue(new S.NPCResponse { ObjectID = ObjectID, Index = page.Index, Values = values });
                break;
            }
        }

        public void NPCCall(PlayerObject ob, string pageKey)
        {
            if (TextInfo?.Script == null) return;

            NPCCall(ob, TextInfo.Script.GetPageCall(pageKey, ob.NPCTextArguments));
        }

        private void NPCCall(PlayerObject ob, CompiledNpcPageCall pageCall)
        {
            int redirects = 0;
            CompiledNpcPage page = pageCall?.Page;
            IReadOnlyList<string> arguments = pageCall?.Arguments;

            while (true)
            {
                if (page == null) return;
                if (++redirects > 64)
                {
                    SEnvir.Log($"[NpcText] GOTO 循环过深，NPC: {Name}, Page: {page.Key}");
                    return;
                }

                CompiledNpcBlock block = CheckTextPage(ob, page, arguments) ? page.MainBlock : page.ElseBlock;
                if (block == null || block.IsEmpty)
                {
                    ob.NPC = null;
                    ob.NPCPage = null;
                    ob.NPCTextPage = null;
                    ob.NPCTextBlock = null;
                    ob.NPCTextArguments = null;
                    ob.Enqueue(new S.NPCClose());
                    return;
                }

                string nextPage = DoTextActions(ob, block, arguments);
                if (!string.IsNullOrWhiteSpace(nextPage))
                {
                    pageCall = TextInfo.Script.GetPageCall(nextPage, arguments);
                    page = pageCall?.Page;
                    arguments = pageCall?.Arguments;
                    continue;
                }

                if (string.IsNullOrEmpty(block.Say))
                {
                    ob.NPC = null;
                    ob.NPCPage = null;
                    ob.NPCTextPage = null;
                    ob.NPCTextBlock = null;
                    ob.NPCTextArguments = null;
                    ob.Enqueue(new S.NPCClose());
                    return;
                }

                ob.NPC = this;
                ob.NPCPage = null;
                ob.NPCTextPage = page;
                ob.NPCTextBlock = block;
                ob.NPCTextArguments = arguments?.ToList();

                ob.Enqueue(new S.NPCResponse { ObjectID = ObjectID, TextPage = page.ToClientPage(block, arguments), Values = [] });
                return;
            }
        }

        private void DoActions(PlayerObject ob, NPCPage page)
        {
            foreach (NPCAction action in page.Actions)
            {
                switch (action.ActionType)
                {
                    case NPCActionType.Teleport:
                        {
                            if (action.MapParameter1 == null && action.InstanceParameter1 == null) continue;

                            if (action.InstanceParameter1 != null)
                            {
                                if (ob.CurrentMap.Instance != null)
                                {
                                    // cannot move from one instance to another
                                    continue;
                                }

                                //TODO - Add conditions to npc instance moving

                                var (index, result) = ob.GetInstance(action.InstanceParameter1);

                                if (result != InstanceResult.Success)
                                {
                                    ob.SendInstanceMessage(action.InstanceParameter1, result);
                                    continue;
                                }

                                if (ob.Teleport(action.InstanceParameter1.ConnectRegion, action.InstanceParameter1, index.Value))
                                {

                                }
                            }
                            else
                            {
                                Map map = SEnvir.GetMap(action.MapParameter1, ob.CurrentMap.Instance, ob.CurrentMap.InstanceSequence);

                                // prevents moving across or off instances
                                if (map != null)
                                {
                                    if (action.IntParameter1 == 0 && action.IntParameter2 == 0)
                                        ob.Teleport(map, map.GetRandomLocation());
                                    else
                                        ob.Teleport(map, new Point(action.IntParameter1, action.IntParameter2));
                                }
                            }
                        }
                        break;
                    case NPCActionType.TakeGold:
                        ob.Gold.Amount -= action.IntParameter1;
                        ob.GoldChanged();
                        break;
                    case NPCActionType.ChangeElement:
                        UserItem weapon = ob.Equipment[(int)EquipmentSlot.Weapon];

                        S.ItemStatsChanged changedResult = new S.ItemStatsChanged { GridType = GridType.Equipment, Slot = (int)EquipmentSlot.Weapon, NewStats = new Stats() };
                        changedResult.NewStats[Stat.WeaponElement] = action.IntParameter1 - weapon.Stats[Stat.WeaponElement];

                        weapon.AddStat(Stat.WeaponElement, action.IntParameter1 - weapon.Stats[Stat.WeaponElement], StatSource.Refine);
                        weapon.StatsChanged();
                        ob.RefreshStats();

                        ob.Enqueue(changedResult);
                        break;
                    case NPCActionType.ChangeHorse:
                        ob.Character.Account.Horse = (HorseType)action.IntParameter1;

                        ob.RemoveMount();

                        ob.RefreshStats();

                        if (ob.Character.Account.Horse != HorseType.None) ob.Mount();
                        break;
                    case NPCActionType.GiveGold:

                        long gold = ob.Gold.Amount + action.IntParameter1;

                        ob.Gold.Amount = gold;
                        ob.LogMilestone(MilestoneType.CurrencyGain, action.IntParameter1, currency: ob.Gold.Info);
                        ob.GoldChanged();

                        break;
                    case NPCActionType.Marriage:
                        ob.MarriageRequest();
                        break;
                    case NPCActionType.Divorce:
                        ob.MarriageLeave();
                        break;
                    case NPCActionType.RemoveWeddingRing:
                        ob.MarriageRemoveRing();
                        break;
                    case NPCActionType.GiveItem:
                        {
                            if (action.ItemParameter1 == null) continue;

                            ItemCheck check = new ItemCheck(action.ItemParameter1, action.IntParameter1, UserItemFlags.None, TimeSpan.Zero);

                            if (!ob.CanGainItems(false, check)) continue;

                            while (check.Count > 0)
                                ob.GainItem(SEnvir.CreateFreshItem(check));
                        }
                        break;
                    case NPCActionType.TakeItem:
                        if (action.ItemParameter1 == null) continue;

                        ob.TakeItem(action.ItemParameter1, action.IntParameter1);
                        break;
                    case NPCActionType.ResetWeapon:
                        ob.NPCResetWeapon();
                        break;
                    case NPCActionType.GiveItemExperience:
                        {
                            if (action.ItemParameter1 == null) continue;

                            var check = new ItemCheck(action.ItemParameter1, action.IntParameter1, UserItemFlags.None, TimeSpan.Zero);

                            if (!ob.CanGainItems(false, check)) continue;

                            while (check.Count > 0)
                            {
                                UserItem item = SEnvir.CreateFreshItem(check);

                                item.Experience = action.IntParameter2;

                                if (item.Experience >= Globals.AccessoryExperienceList[item.Level])
                                {
                                    item.Experience -= Globals.AccessoryExperienceList[item.Level];
                                    item.Level++;

                                    item.Flags |= UserItemFlags.Refinable;
                                }

                                ob.GainItem(item);
                            }
                        }
                        break;
                    case NPCActionType.SpecialRefine:
                        ob.NPCSpecialRefine(action.StatParameter1, action.IntParameter1);
                        break;
                    case NPCActionType.Rebirth:
                        if (ob.Level >= 86 + ob.Character.Rebirth)
                            ob.NPCRebirth();
                        break;
                    case NPCActionType.PromoteFame:
                        ob.PromoteFame();
                        break;
                    case NPCActionType.GiveCurrency:
                        {
                            if (action.StringParameter1 == null) continue;

                            var info = SEnvir.CurrencyInfoList.Binding.FirstOrDefault(x => string.Equals(x.Name, action.StringParameter1, StringComparison.OrdinalIgnoreCase));
                            if (info == null) continue;

                            var userCurrency = ob.GetCurrency(info);

                            var amount = userCurrency.Amount + action.IntParameter1;

                            userCurrency.Amount = amount;
                            ob.LogMilestone(MilestoneType.CurrencyGain, action.IntParameter1, currency: userCurrency.Info);
                            ob.CurrencyChanged(userCurrency);
                        }
                        break;
                    case NPCActionType.TakeCurrency:
                        {
                            if (action.StringParameter1 == null) continue;

                            var info = SEnvir.CurrencyInfoList.Binding.FirstOrDefault(x => string.Equals(x.Name, action.StringParameter1, StringComparison.OrdinalIgnoreCase));
                            if (info == null) continue;

                            var userCurrency = ob.GetCurrency(info);

                            var amount = userCurrency.Amount - action.IntParameter1;

                            userCurrency.Amount = amount;
                            ob.CurrencyChanged(userCurrency);
                        }
                        break;
                    case NPCActionType.AddDataList:
                        {
                            if (action.StringParameter1 == null) continue;

                            var category = $"{action.StringParameter1}_NameList";
                            var value1 = GetDataTypeValue(ob, (NPCDataType)action.IntParameter1);

                            if (string.IsNullOrEmpty(value1)) continue;

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            if (item == null)
                            {
                                item = SEnvir.GameNPCList.CreateNewObject();
                                item.Category = category;
                                item.TypeValue = value1;
                            }
                        }
                        break;
                    case NPCActionType.RemoveDataList:
                        {
                            if (action.StringParameter1 == null) continue;

                            var category = $"{action.StringParameter1}_NameList";
                            var value1 = GetDataTypeValue(ob, (NPCDataType)action.IntParameter1);

                            if (string.IsNullOrEmpty(value1)) continue;

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            item?.Delete();
                        }
                        break;
                    case NPCActionType.ClearDataList:
                        {
                            if (action.StringParameter1 == null) continue;

                            var category = $"{action.StringParameter1}_NameList";

                            for (int i = SEnvir.GameNPCList.Binding.Count - 1; i >= 0; i--)
                            {
                                var item = SEnvir.GameNPCList.Binding[i];
                                if (item.Category != category) continue;

                                item.Delete();
                            }
                        }
                        break;
                    case NPCActionType.ChangeDataValue:
                        {
                            if (action.StringParameter1 == null) continue;

                            var category = action.StringParameter1;
                            var value1 = GetDataTypeValue(ob, (NPCDataType)action.IntParameter1);

                            if (string.IsNullOrEmpty(value1)) continue;

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            if (item == null)
                            {
                                item = SEnvir.GameNPCList.CreateNewObject();
                                item.Category = category;
                                item.TypeValue = value1;
                            }

                            item.IntValue1 += action.IntParameter2;
                        }
                        break;
                    case NPCActionType.SetDataValue:
                        {
                            if (action.StringParameter1 == null) continue;

                            var category = action.StringParameter1;
                            var value1 = GetDataTypeValue(ob, (NPCDataType)action.IntParameter1);

                            if (string.IsNullOrEmpty(value1)) continue;

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            if (item == null)
                            {
                                item = SEnvir.GameNPCList.CreateNewObject();
                                item.Category = category;
                                item.TypeValue = value1;
                            }

                            item.IntValue1 = action.IntParameter2;
                        }
                        break;
                }
            }
        }

        private string DoTextActions(PlayerObject ob, CompiledNpcBlock block, IReadOnlyList<string> arguments)
        {
            string nextPage = null;

            foreach (CompiledNpcAction action in block.Actions)
            {
                switch (action.ActionType)
                {
                    case CompiledNpcActionType.Goto:
                        nextPage = CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments);
                        break;
                    case CompiledNpcActionType.GiveGold:
                        ob.Gold.Amount += action.IntParameter1;
                        ob.LogMilestone(MilestoneType.CurrencyGain, action.IntParameter1, currency: ob.Gold.Info);
                        ob.GoldChanged();
                        break;
                    case CompiledNpcActionType.TakeGold:
                        ob.Gold.Amount -= action.IntParameter1;
                        ob.GoldChanged();
                        break;
                    case CompiledNpcActionType.GiveCurrency:
                    case CompiledNpcActionType.TakeCurrency:
                        ChangeCurrency(ob, CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments), action.IntParameter1, action.ActionType == CompiledNpcActionType.GiveCurrency);
                        break;
                    case CompiledNpcActionType.GiveItem:
                        {
                            if (action.ItemParameter1 == null) continue;

                            ItemCheck check = new(action.ItemParameter1, action.IntParameter1, UserItemFlags.None, TimeSpan.Zero);
                            if (!ob.CanGainItems(false, check)) continue;

                            while (check.Count > 0)
                                ob.GainItem(SEnvir.CreateFreshItem(check));
                        }
                        break;
                    case CompiledNpcActionType.TakeItem:
                        if (action.ItemParameter1 != null)
                            ob.TakeItem(action.ItemParameter1, action.IntParameter1);
                        break;
                    case CompiledNpcActionType.Move:
                        {
                            Map map = SEnvir.GetMap(action.MapParameter1, ob.CurrentMap.Instance, ob.CurrentMap.InstanceSequence);
                            if (map != null)
                                ob.Teleport(map, new Point(action.IntParameter1, action.IntParameter2));
                        }
                        break;
                    case CompiledNpcActionType.AddNameList:
                        SetDataList(ob, CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments), action.DataType, true);
                        break;
                    case CompiledNpcActionType.DeleteNameList:
                        SetDataList(ob, CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments), action.DataType, false);
                        break;
                    case CompiledNpcActionType.ClearNameList:
                        ClearDataList(CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments));
                        break;
                    case CompiledNpcActionType.SetValue:
                        SetDataValue(ob, CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments), action.DataType, action.IntParameter1, false);
                        break;
                    case CompiledNpcActionType.ChangeValue:
                        SetDataValue(ob, CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments), action.DataType, action.IntParameter1, true);
                        break;
                    case CompiledNpcActionType.LocalMessage:
                        if (!string.IsNullOrWhiteSpace(action.StringParameter1))
                            ob.Connection.ReceiveChat(CompiledNpcScript.ReplaceArguments(action.StringParameter1, arguments), MessageType.System);
                        break;
                    case CompiledNpcActionType.PromoteFame:
                        ob.PromoteFame();
                        break;
                }
            }

            return nextPage;
        }

        private bool CheckTextPage(PlayerObject ob, CompiledNpcPage page, IReadOnlyList<string> arguments)
        {
            foreach (CompiledNpcCheck check in page.Checks)
            {
                switch (check.CheckType)
                {
                    case CompiledNpcCheckType.Level:
                        if (!Compare(check.Operator, ob.Level, check.IntParameter1)) return false;
                        break;
                    case CompiledNpcCheckType.Gold:
                        if (!Compare(check.Operator, ob.Gold.Amount, check.IntParameter1)) return false;
                        break;
                    case CompiledNpcCheckType.Currency:
                        if (!Compare(check.Operator, GetCurrencyAmount(ob, CompiledNpcScript.ReplaceArguments(check.StringParameter1, arguments)), check.IntParameter1)) return false;
                        break;
                    case CompiledNpcCheckType.Fame:
                        if (!CanPromoteFame(ob)) return false;
                        break;
                    case CompiledNpcCheckType.Item:
                        if (check.ItemParameter1 == null) continue;
                        if (!Compare(check.Operator, ob.GetItemCount(check.ItemParameter1), check.IntParameter1)) return false;
                        break;
                    case CompiledNpcCheckType.CanGainItem:
                        if (check.ItemParameter1 == null) continue;
                        ItemCheck itemCheck = new(check.ItemParameter1, check.IntParameter1, UserItemFlags.None, TimeSpan.Zero);
                        if (!ob.CanGainItems(false, itemCheck)) return false;
                        break;
                    case CompiledNpcCheckType.Weapon:
                        if (ob.Equipment[(int)EquipmentSlot.Weapon] != null != (check.Operator == Operator.Equal)) return false;
                        break;
                    case CompiledNpcCheckType.WeaponCanRefine:
                        UserItem weapon = ob.Equipment[(int)EquipmentSlot.Weapon];
                        bool canRefine = weapon != null && (weapon.Flags & UserItemFlags.Refinable) == UserItemFlags.Refinable;
                        if (canRefine != (check.Operator == Operator.Equal)) return false;
                        break;
                    case CompiledNpcCheckType.Class:
                        if (!HasRequiredClass(check.Class, ob.Class)) return false;
                        break;
                    case CompiledNpcCheckType.Gender:
                        if (!HasRequiredGender(check.Gender, ob.Gender)) return false;
                        break;
                    case CompiledNpcCheckType.DayOfWeek:
                        var flag = (DaysOfWeek)Enum.ToObject(typeof(DaysOfWeek), 1 << (int)SEnvir.Now.DayOfWeek);
                        if (check.Days != DaysOfWeek.None && !check.Days.HasFlag(flag)) return false;
                        break;
                    case CompiledNpcCheckType.Hour:
                        if (!Compare(check.Operator, SEnvir.Now.Hour, check.IntParameter1)) return false;
                        break;
                    case CompiledNpcCheckType.Minute:
                        if (!Compare(check.Operator, SEnvir.Now.Minute, check.IntParameter1)) return false;
                        break;
                    case CompiledNpcCheckType.PKPoint:
                        if (!Compare(check.Operator, ob.Stats[Stat.PKPoint], check.IntParameter1)) return false;
                        break;
                    case CompiledNpcCheckType.Random:
                        if (check.IntParameter1 <= 0 || !Compare(check.Operator, SEnvir.Random.Next(check.IntParameter1), check.IntParameter2)) return false;
                        break;
                    case CompiledNpcCheckType.NameList:
                        if (!CheckDataList(ob, CompiledNpcScript.ReplaceArguments(check.StringParameter1, arguments), check.DataType)) return false;
                        break;
                    case CompiledNpcCheckType.Value:
                        if (!Compare(check.Operator, GetDataValue(ob, CompiledNpcScript.ReplaceArguments(check.StringParameter1, arguments), check.DataType), check.IntParameter1)) return false;
                        break;
                }
            }

            return true;
        }

        private static long GetCurrencyAmount(PlayerObject ob, string currencyName)
        {
            if (string.IsNullOrWhiteSpace(currencyName)) return 0;

            CurrencyInfo info = SEnvir.CurrencyInfoList.Binding.FirstOrDefault(x => string.Equals(x.Name, currencyName, StringComparison.OrdinalIgnoreCase));
            return info == null ? 0 : ob.GetCurrency(info).Amount;
        }

        private static void ChangeCurrency(PlayerObject ob, string currencyName, int amount, bool add)
        {
            if (string.IsNullOrWhiteSpace(currencyName)) return;

            CurrencyInfo info = SEnvir.CurrencyInfoList.Binding.FirstOrDefault(x => string.Equals(x.Name, currencyName, StringComparison.OrdinalIgnoreCase));
            if (info == null) return;

            UserCurrency currency = ob.GetCurrency(info);
            currency.Amount += add ? amount : -amount;

            if (add)
                ob.LogMilestone(MilestoneType.CurrencyGain, amount, currency: currency.Info);

            ob.CurrencyChanged(currency);
        }

        private static bool CanPromoteFame(PlayerObject ob)
        {
            var nextFame = ob.GetNextFameTitle();
            if (nextFame == null) return false;

            var currency = SEnvir.CurrencyInfoList.Binding.FirstOrDefault(x => x.Type == CurrencyType.FP);
            if (currency == null) return false;

            return ob.GetCurrency(currency).Amount >= nextFame.Cost;
        }

        private static bool HasRequiredClass(RequiredClass requiredClass, MirClass mirClass)
        {
            if (requiredClass == RequiredClass.None || requiredClass == RequiredClass.All) return true;

            return mirClass switch
            {
                MirClass.Warrior => (requiredClass & RequiredClass.Warrior) == RequiredClass.Warrior,
                MirClass.Wizard => (requiredClass & RequiredClass.Wizard) == RequiredClass.Wizard,
                MirClass.Taoist => (requiredClass & RequiredClass.Taoist) == RequiredClass.Taoist,
                MirClass.Assassin => (requiredClass & RequiredClass.Assassin) == RequiredClass.Assassin,
                _ => false
            };
        }

        private static bool HasRequiredGender(RequiredGender requiredGender, MirGender mirGender)
        {
            if (requiredGender == RequiredGender.None) return true;

            return mirGender switch
            {
                MirGender.Male => (requiredGender & RequiredGender.Male) == RequiredGender.Male,
                MirGender.Female => (requiredGender & RequiredGender.Female) == RequiredGender.Female,
                _ => false
            };
        }

        private static bool CheckDataList(PlayerObject ob, string categoryName, NPCDataType dataType)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return false;

            string category = $"{categoryName}_NameList";
            string value = GetDataTypeValue(ob, dataType);
            if (string.IsNullOrEmpty(value)) return false;

            return SEnvir.GameNPCList.Binding.Any(x => x.Category == category && x.TypeValue == value);
        }

        private static int GetDataValue(PlayerObject ob, string categoryName, NPCDataType dataType)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return 0;

            string value = GetDataTypeValue(ob, dataType);
            if (string.IsNullOrEmpty(value)) return 0;

            return SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == categoryName && x.TypeValue == value)?.IntValue1 ?? 0;
        }

        private static void SetDataList(PlayerObject ob, string categoryName, NPCDataType dataType, bool add)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return;

            string category = $"{categoryName}_NameList";
            string value = GetDataTypeValue(ob, dataType);
            if (string.IsNullOrEmpty(value)) return;

            GameNPCList item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value);
            if (add)
            {
                if (item != null) return;

                item = SEnvir.GameNPCList.CreateNewObject();
                item.Category = category;
                item.TypeValue = value;
                return;
            }

            item?.Delete();
        }

        private static void ClearDataList(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return;

            string category = $"{categoryName}_NameList";

            for (int i = SEnvir.GameNPCList.Binding.Count - 1; i >= 0; i--)
            {
                GameNPCList item = SEnvir.GameNPCList.Binding[i];
                if (item.Category != category) continue;

                item.Delete();
            }
        }

        private static void SetDataValue(PlayerObject ob, string categoryName, NPCDataType dataType, int amount, bool add)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return;

            string value = GetDataTypeValue(ob, dataType);
            if (string.IsNullOrEmpty(value)) return;

            GameNPCList item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == categoryName && x.TypeValue == value);
            if (item == null)
            {
                item = SEnvir.GameNPCList.CreateNewObject();
                item.Category = categoryName;
                item.TypeValue = value;
            }

            if (add)
                item.IntValue1 += amount;
            else
                item.IntValue1 = amount;
        }

        private static List<ClientNPCValues> GetValues(PlayerObject ob, NPCPage page)
        {
            List<ClientNPCValues> values = new();

            foreach (NPCValue value in page.Values)
            {
                switch (value.ValueType)
                {
                    case NPCValueType.DataList:
                        {
                            if (value.DataCategory == null) continue;

                            var category = $"{value.DataCategory}_NameList";
                            var value1 = GetDataTypeValue(ob, value.DataType);

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            string value2 = "True";

                            if (item != null)
                            {
                                values.Add(new ClientNPCValues { ID = value.ValueID, Value = value2 });
                            }
                        }
                        break;
                    case NPCValueType.DataValue:
                        {
                            if (value.DataCategory == null) continue;

                            var category = $"{value.DataCategory}";
                            var value1 = GetDataTypeValue(ob, value.DataType);

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            if (item != null)
                            {
                                values.Add(new ClientNPCValues { ID = value.ValueID, Value = item.IntValue1.ToString() });
                            }
                        }
                        break;
                    case NPCValueType.Field:
                        {
                            string value2 = "";

                            switch (value.FieldType)
                            {
                                case NPCFieldType.Name:
                                    value2 = ob.Name;
                                    break;
                                case NPCFieldType.GuildName:
                                    value2 = ob.Character.Account.GuildMember?.Guild?.GuildName ?? null;
                                    break;
                                case NPCFieldType.FameCost:
                                    {
                                        var fame = ob.GetNextFameTitle();
                                        value2 = fame?.Cost.ToString();
                                    }
                                    break;
                                case NPCFieldType.None:
                                    continue;
                            }

                            if (!string.IsNullOrEmpty(value2))
                            {
                                values.Add(new ClientNPCValues { ID = value.ValueID, Value = value2 });
                            }
                        }
                        break;
                    case NPCValueType.RollResult:
                        {
                            if (ob.NPCVals.TryGetValue("ROLLRESULT", out object val))
                            {
                                values.Add(new ClientNPCValues { ID = value.ValueID, Value = val.ToString() });
                            }
                        }
                        break;
                }
            }

            return values;
        }

        private bool CheckPage(PlayerObject ob, NPCPage page, out NPCPage failPage)
        {
            failPage = null;
            foreach (NPCCheck check in page.Checks)
            {
                failPage = check.FailPage;
                UserItem weap;
                switch (check.CheckType)
                {
                    case NPCCheckType.Level:
                        if (!Compare(check.Operator, ob.Level, check.IntParameter1)) return false;
                        break;
                    case NPCCheckType.Class:
                        if (!Compare(check.Operator, (int)ob.Class, check.IntParameter1)) return false;
                        break;
                    case NPCCheckType.Gender:
                        if (!Compare(check.Operator, (int)ob.Gender, check.IntParameter1)) return false;
                        break;
                    case NPCCheckType.Gold:
                        if (!Compare(check.Operator, ob.Gold.Amount, check.IntParameter1)) return false;
                        break;

                    case NPCCheckType.HasWeapon:
                        if (ob.Equipment[(int)EquipmentSlot.Weapon] != null != (check.Operator == Operator.Equal)) return false;
                        break;

                    case NPCCheckType.WeaponLevel:
                        weap = ob.Equipment[(int)EquipmentSlot.Weapon];
                        if (weap == null || !Compare(check.Operator, weap.Level, check.IntParameter1)) return false;
                        break;

                    case NPCCheckType.WeaponCanRefine:
                        weap = ob.Equipment[(int)EquipmentSlot.Weapon];
                        if ((weap != null && (weap.Flags & UserItemFlags.Refinable) == UserItemFlags.Refinable) != (check.Operator == Operator.Equal)) return false;
                        break;
                    case NPCCheckType.WeaponAddedStats:
                        weap = ob.Equipment[(int)EquipmentSlot.Weapon];
                        if (weap == null || !Compare(check.Operator, weap.Stats[check.StatParameter1], check.IntParameter1)) return false;
                        break;
                    case NPCCheckType.WeaponElement:
                        weap = ob.Equipment[(int)EquipmentSlot.Weapon];
                        if (weap == null) return false;

                        Stat element;
                        int value = 0;

                        switch ((Element)check.IntParameter1)
                        {
                            case Element.None:
                                value += weap.Stats.GetWeaponElementValue();
                                value += weap.Info.Stats.GetWeaponElementValue();
                                break;
                            case Element.Fire:
                                element = weap.Stats.GetWeaponElement();

                                if (element == Stat.None)
                                    element = weap.Info.Stats.GetWeaponElement();

                                if (element == Stat.FireAttack)
                                {
                                    value += weap.Stats.GetWeaponElementValue();
                                    value += weap.Info.Stats.GetWeaponElementValue();
                                }

                                break;
                            case Element.Ice:
                                element = weap.Stats.GetWeaponElement();

                                if (element == Stat.None)
                                    element = weap.Info.Stats.GetWeaponElement();

                                if (element == Stat.IceAttack)
                                {
                                    value += weap.Stats.GetWeaponElementValue();
                                    value += weap.Info.Stats.GetWeaponElementValue();
                                }

                                break;
                            case Element.Lightning:
                                element = weap.Stats.GetWeaponElement();

                                if (element == Stat.None)
                                    element = weap.Info.Stats.GetWeaponElement();

                                if (element == Stat.LightningAttack)
                                {
                                    value += weap.Stats.GetWeaponElementValue();
                                    value += weap.Info.Stats.GetWeaponElementValue();
                                }

                                break;
                            case Element.Wind:
                                element = weap.Stats.GetWeaponElement();

                                if (element == Stat.None)
                                    element = weap.Info.Stats.GetWeaponElement();

                                if (element == Stat.WindAttack)
                                {
                                    value += weap.Stats.GetWeaponElementValue();
                                    value += weap.Info.Stats.GetWeaponElementValue();
                                }

                                break;
                            case Element.Holy:
                                element = weap.Stats.GetWeaponElement();

                                if (element == Stat.None)
                                    element = weap.Info.Stats.GetWeaponElement();

                                if (element == Stat.HolyAttack)
                                {
                                    value += weap.Stats.GetWeaponElementValue();
                                    value += weap.Info.Stats.GetWeaponElementValue();
                                }

                                break;
                            case Element.Dark:
                                element = weap.Stats.GetWeaponElement();

                                if (element == Stat.None)
                                    element = weap.Info.Stats.GetWeaponElement();

                                if (element == Stat.DarkAttack)
                                {
                                    value += weap.Stats.GetWeaponElementValue();
                                    value += weap.Info.Stats.GetWeaponElementValue();
                                }

                                break;
                            case Element.Phantom:
                                element = weap.Stats.GetWeaponElement();

                                if (element == Stat.None)
                                    element = weap.Info.Stats.GetWeaponElement();

                                if (element == Stat.PhantomAttack)
                                {
                                    value += weap.Stats.GetWeaponElementValue();
                                    value += weap.Info.Stats.GetWeaponElementValue();
                                }
                                break;
                        }

                        if (!Compare(check.Operator, value, check.IntParameter2)) return false;
                        break;
                    case NPCCheckType.PKPoints:
                        if (!Compare(check.Operator, ob.Stats[Stat.PKPoint], check.IntParameter1 == 0 ? Config.RedPoint : check.IntParameter1) && ob.Stats[Stat.Redemption] == 0)
                            return false;
                        break;
                    case NPCCheckType.Horse:
                        if (!Compare(check.Operator, (int)ob.Character.Account.Horse, check.IntParameter1)) return false;
                        break;
                    case NPCCheckType.Marriage:
                        if (check.Operator == Operator.Equal)
                        {
                            if (ob.Character.Partner == null) return false;
                        }
                        else
                        {
                            if (ob.Character.Partner != null) return false;
                        }
                        break;
                    case NPCCheckType.WeddingRing:
                        if (check.Operator == Operator.Equal)
                        {
                            if (ob.Equipment[(int)EquipmentSlot.RingL] == null || (ob.Equipment[(int)EquipmentSlot.RingL].Flags & UserItemFlags.Marriage) != UserItemFlags.Marriage) return false;
                        }
                        else
                        {
                            if (ob.Equipment[(int)EquipmentSlot.RingL] != null && (ob.Equipment[(int)EquipmentSlot.RingL].Flags & UserItemFlags.Marriage) == UserItemFlags.Marriage) return false;
                        }
                        break;
                    case NPCCheckType.HasItem:
                        if (check.ItemParameter1 == null) continue;
                        if (!Compare(check.Operator, ob.GetItemCount(check.ItemParameter1), check.IntParameter1)) return false;
                        break;
                    case NPCCheckType.CanGainItem:
                        if (check.ItemParameter1 == null) continue;

                        ItemCheck itemCheck = new ItemCheck(check.ItemParameter1, check.IntParameter1, UserItemFlags.None, TimeSpan.Zero);

                        if (!ob.CanGainItems(false, itemCheck)) return false;
                        break;
                    case NPCCheckType.CanResetWeapon:
                        weap = ob.Equipment[(int)EquipmentSlot.Weapon];
                        if (weap == null) return false;

                        if (check.Operator == Operator.Equal)
                        {
                            if (SEnvir.Now < weap.ResetCoolDown) return false;
                        }
                        else
                        {
                            if (SEnvir.Now >= weap.ResetCoolDown) return false;
                        }
                        break;
                    case NPCCheckType.Random:
                        if (!Compare(check.Operator, SEnvir.Random.Next(check.IntParameter1), check.IntParameter2)) return false;
                        break;

                    case NPCCheckType.Currency:
                        {
                            if (check.StringParameter1 == null) continue;

                            var info = SEnvir.CurrencyInfoList.Binding.FirstOrDefault(x => string.Equals(x.Name, check.StringParameter1, StringComparison.OrdinalIgnoreCase));
                            if (info == null) continue;

                            var userCurrency = ob.GetCurrency(info);

                            if (!Compare(check.Operator, userCurrency.Amount, check.IntParameter1)) return false;
                        }
                        break;
                    case NPCCheckType.RollResult:
                        {
                            if (!ob.NPCVals.TryGetValue("ROLLRESULT", out object val)) return false;
                            if (!Compare(check.Operator, (int)val, check.IntParameter1)) return false;
                        }
                        break;
                    case NPCCheckType.CheckDataList:
                        {
                            if (check.StringParameter1 == null) continue;

                            var category = $"{check.StringParameter1}_NameList";
                            var value1 = GetDataTypeValue(ob, (NPCDataType)check.IntParameter1);

                            if (string.IsNullOrEmpty(value1)) continue;

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            if (item == null) return false;
                        }
                        break;
                    case NPCCheckType.CheckDataValue:
                        {
                            if (check.StringParameter1 == null) continue;

                            var category = $"{check.StringParameter1}";
                            var value1 = GetDataTypeValue(ob, (NPCDataType)check.IntParameter1);

                            if (string.IsNullOrEmpty(value1)) continue;

                            var val = 0;

                            var item = SEnvir.GameNPCList.Binding.FirstOrDefault(x => x.Category == category && x.TypeValue == value1);

                            if (item != null)
                            {
                                val = item.IntValue1;
                            }

                            if (!Compare(check.Operator, (int)val, check.IntParameter2)) return false;
                        }
                        break;
                    case NPCCheckType.CheckFame:
                        {
                            var nextFame = ob.GetNextFameTitle();

                            var currency = SEnvir.CurrencyInfoList.Binding.FirstOrDefault(x => x.Type == CurrencyType.FP);

                            if (currency == null) return false;

                            var userCurrency = ob.GetCurrency(currency);

                            if (nextFame == null || nextFame.Cost > userCurrency.Amount) return false;
                        }
                        break;
                }
            }
            return true;
        }

        private bool Compare(Operator op, long pValue, long cValue)
        {
            switch (op)
            {
                case Operator.Equal:
                    return pValue == cValue;
                case Operator.NotEqual:
                    return pValue != cValue;
                case Operator.LessThan:
                    return pValue < cValue;
                case Operator.LessThanOrEqual:
                    return pValue <= cValue;
                case Operator.GreaterThan:
                    return pValue > cValue;
                case Operator.GreaterThanOrEqual:
                    return pValue >= cValue;
                default: return false;
            }
        }

        private static string GetDataTypeValue(PlayerObject ob, NPCDataType type)
        {
            string value = null;

            switch (type)
            {
                case NPCDataType.None:
                    value = $"{type}";
                    break;
                case NPCDataType.User:
                    value = $"{type}_{ob.Name}";
                    break;
                case NPCDataType.Guild:
                    if (ob.Character.Account.GuildMember?.Guild?.GuildName != null)
                        value = $"{type}_{ob.Character.Account.GuildMember.Guild.GuildName}";
                    break;
                case NPCDataType.Account:
                    value = $"{type}_{ob.Character.Account.EMailAddress}";
                    break;
            }

            return value;
        }

        public override Packet GetInfoPacket(PlayerObject ob)
        {
            return new S.ObjectNPC
            {
                ObjectID = ObjectID,

                NPCIndex = NPCInfo?.Index ?? 0,
                TextNPC = TextInfo?.ToClientInfo(),

                CurrentLocation = CurrentLocation,

                Direction = Direction,
            };
        }

        public override bool CanBeSeenBy(PlayerObject ob)
        {
            if (TextInfo != null)
            {
                if (TextInfo.MinLevel > 0 && ob.Level < TextInfo.MinLevel) return false;
                if (TextInfo.MaxLevel > 0 && ob.Level > TextInfo.MaxLevel) return false;
                if (!HasRequiredClass(TextInfo.Class, ob.Class)) return false;

                var flag = (DaysOfWeek)Enum.ToObject(typeof(DaysOfWeek), 1 << (int)DateTime.UtcNow.DayOfWeek);
                if (TextInfo.Days != DaysOfWeek.None && !TextInfo.Days.HasFlag(flag)) return false;
            }

            if (NPCInfo != null)
            {
                foreach (NPCRequirement requirement in NPCInfo.Requirements)
                {
                    switch (requirement.Requirement)
                    {
                        case NPCRequirementType.MaxLevel:
                            if (ob.Level > requirement.IntParameter1) return false;
                            break;
                        case NPCRequirementType.MinLevel:
                            if (ob.Level < requirement.IntParameter1) return false;
                            break;
                        case NPCRequirementType.Accepted:
                            if (ob.Quests.Any(x => x.QuestInfo == requirement.QuestParameter)) break;

                            return false;
                        case NPCRequirementType.NotAccepted:
                            if (ob.Quests.Any(x => x.QuestInfo == requirement.QuestParameter)) return false;

                            break;
                        case NPCRequirementType.HaveCompleted:
                            if (ob.Quests.Any(x => x.QuestInfo == requirement.QuestParameter && x.Completed)) break;

                            return false;
                        case NPCRequirementType.HaveNotCompleted:
                            if (ob.Quests.Any(x => x.QuestInfo == requirement.QuestParameter && x.Completed)) return false;

                            break;
                        case NPCRequirementType.Class:
                            if (!HasRequiredClass(requirement.Class, ob.Class)) return false;
                            break;
                        case NPCRequirementType.DaysOfWeek:
                            var flag = (DaysOfWeek)Enum.ToObject(typeof(DaysOfWeek), 1 << (int)DateTime.UtcNow.DayOfWeek);

                            if (!requirement.DaysOfWeek.HasFlag(flag)) return false;
                            break;
                    }
                }
            }

            return Visible && base.CanBeSeenBy(ob);
        }

        public override bool CanDataBeSeenBy(PlayerObject ob)
        {
            return false;
        }

        public override Packet GetDataPacket(PlayerObject ob)
        {
            return null;
        }
    }
}
