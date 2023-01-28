﻿using E3Core.Data;
using E3Core.Processors;
using E3Core.Settings;
using IniParser;
using MonoCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static MonoCore.EventProcessor;


namespace E3Core.Utility
{
    public static class e3util
    {

        public static string _lastSuccesfulCast = String.Empty;
        public static Logging _log = E3.Log;
        private static IMQ MQ = E3.MQ;
        private static ISpawns _spawns = E3.Spawns;
        /// <summary>
        /// Use to see if a certain method should be running
        /// </summary>
        /// <param name="nextCheck">ref param to update ot the next time a thing should run</param>
        /// <param name="nextCheckInterval">The interval in milliseconds</param>
        /// <returns></returns>
        public static bool ShouldCheck(ref Int64 nextCheck, Int64 nextCheckInterval)
        {  
            if (Core.StopWatch.ElapsedMilliseconds < nextCheck)
            {
                return false;
            }
            else
            {
                nextCheck = Core.StopWatch.ElapsedMilliseconds + nextCheckInterval;
                return true;
            }
        }

        public static void TryMoveToTarget()
        {
            //Check for Nav path if available and Nav is loaded
            bool navLoaded = MQ.Query<bool>("${Plugin[MQ2Nav].IsLoaded}");
            int targetID = MQ.Query<int>("${Target.ID}");
            
            if (navLoaded)
            {
                if (MQ.Query<Double>("${Target.Distance}") < 100 && MQ.Query<bool>("${Target.LineOfSight}"))
                {
                    goto UseMoveTo;
                }
                bool meshLoaded = MQ.Query<bool>("${Navigation.MeshLoaded}");

                if (meshLoaded)
                {
                    
                    e3util.NavToSpawnID(targetID);
                    //exit from TryMoveToTarget if we've reached the target
                    if(MQ.Query<Double>("${Target.Distance}") < E3.GeneralSettings.Movement_NavStopDistance)
                    {
                        return;
                    }
                }
            }
            
            
            if (!MQ.Query<bool>("${Target.LineOfSight}"))
            {
                E3.Bots.Broadcast("\arCannot move to target, not in LoS");
                E3.Bots.BroadcastCommand("/popup ${Me} cannot move to ${Target}, not in LoS", false);
                MQ.Cmd("/beep");
                return;
            }

            UseMoveTo:
            Double meX = MQ.Query<Double>("${Me.X}");
            Double meY = MQ.Query<Double>("${Me.Y}");

            Double x = MQ.Query<Double>("${Target.X}");
            Double y = MQ.Query<Double>("${Target.Y}");
            MQ.Cmd($"/squelch /moveto loc {y} {x} mdist 5");
            MQ.Delay(500);

            Int64 endTime = Core.StopWatch.ElapsedMilliseconds + 10000;
            while(true)
            {
               
                Double tmeX = MQ.Query<Double>("${Me.X}");
                Double tmeY = MQ.Query<Double>("${Me.Y}");

                if((int)meX==(int)tmeX && (int)meY==(int)tmeY)
                {
                    //we are stuck, kick out
                    break;
                }

                meX = tmeX;
                meY = tmeY;

                if (endTime < Core.StopWatch.ElapsedMilliseconds)
                {
                    break;
                }
                MQ.Delay(200);
            }

        }
        public static bool IsManualControl()
        {
            var isInForeground = MQ.Query<bool>("${EverQuest.Foreground}");
            if (isInForeground) return true;

            return false;
        }
        public static bool FilterMe(CommandMatch x)
        {
            ////Stop /Only|Soandoso
            ////FollowOn /Only|Healers WIZ Soandoso
            ////followon /Not|Healers /Exclude|Uberhealer1
            /////Staunch /Only|Healers
            /////Follow /Not|MNK
            //things like this put into the filter collection.
            //process the filters for commands
            bool returnValue = false;

            //get any 'only' filter.
            //get any 'include/exclude' filter with it.
            string onlyFilter = string.Empty;
            string notFilter = String.Empty;
            string excludeFilter = string.Empty;
            string includeFilter = String.Empty;
            foreach (var filter in x.filters)
            {
                if (filter.StartsWith("/only", StringComparison.OrdinalIgnoreCase))
                {
                    onlyFilter = filter;
                }
                if (filter.StartsWith("/not", StringComparison.OrdinalIgnoreCase))
                {
                    notFilter = filter;
                }
                if (filter.StartsWith("/exclude", StringComparison.OrdinalIgnoreCase))
                {
                    excludeFilter = filter;
                }
                if (filter.StartsWith("/include", StringComparison.OrdinalIgnoreCase))
                {
                    includeFilter = filter;
                }
            }

            List<string> includeInputs = new List<string>();
            List<string> excludeInputs = new List<string>();
            //get the include/exclude values first before we process /not/only

            if (onlyFilter != string.Empty)
            {
                //assume we are excluded unless we match with an only filter
                returnValue = true;

                Int32 indexOfPipe = onlyFilter.IndexOf('|') + 1;
                string input = onlyFilter.Substring(indexOfPipe, onlyFilter.Length - indexOfPipe);
                //now split up into a list of values.
                List<string> inputs = StringsToList(input, ' ');

                if (!FilterReturnCheck(inputs, ref returnValue, false))
                {
                    return false;
                }
               
                if (includeFilter != String.Empty)
                {
                    indexOfPipe = includeFilter.IndexOf('|') + 1;
                    string icludeInput = includeFilter.Substring(indexOfPipe, includeFilter.Length - indexOfPipe);
                    includeInputs = StringsToList(icludeInput, ' ');

                    if (!FilterReturnCheck(includeInputs, ref returnValue, false))
                    {
                        return false;
                    }
                }
                if (excludeFilter != String.Empty)
                {
                    indexOfPipe = excludeFilter.IndexOf('|') + 1;
                    string excludeInput = excludeFilter.Substring(indexOfPipe, excludeFilter.Length - indexOfPipe);
                    excludeInputs = StringsToList(excludeInput, ' ');

                    if (FilterReturnCheck(excludeInputs, ref returnValue, true))
                    {
                        return true;
                    }
                }

            }
            else if (notFilter != string.Empty)
            {
                returnValue = false;
                 Int32 indexOfPipe = notFilter.IndexOf('|') + 1;
                string input = notFilter.Substring(indexOfPipe, notFilter.Length - indexOfPipe);
                //now split up into a list of values.
                List<string> inputs = StringsToList(input, ' ');

                if (FilterReturnCheck(inputs, ref returnValue, true))
                {
                    return true;
                }

                if (includeFilter != String.Empty)
                {
                    indexOfPipe = includeFilter.IndexOf('|') + 1;
                    string icludeInput = includeFilter.Substring(indexOfPipe, includeFilter.Length - indexOfPipe);
                    includeInputs = StringsToList(icludeInput, ' ');

                    if (!FilterReturnCheck(includeInputs, ref returnValue, false))
                    {
                        return false;
                    }
                }
                if (excludeFilter != String.Empty)
                {
                    indexOfPipe = excludeFilter.IndexOf('|') + 1;
                    string excludeInput = excludeFilter.Substring(indexOfPipe, excludeFilter.Length - indexOfPipe);
                    excludeInputs = StringsToList(excludeInput, ' ');

                    if (FilterReturnCheck(excludeInputs, ref returnValue, true))
                    {
                        return true;
                    }
                }
            }


            return returnValue;
        }

        private static bool FilterReturnCheck(List<string> inputs, ref bool returnValue, bool inputSetValue)
        {
            if (inputs.Contains(E3.CurrentName, StringComparer.OrdinalIgnoreCase))
            {
                return inputSetValue;
            }
            if (inputs.Contains(E3.CurrentShortClassString, StringComparer.OrdinalIgnoreCase))
            {
                returnValue = inputSetValue;
                return returnValue;
            }
            if (inputs.Contains("Healers", StringComparer.OrdinalIgnoreCase))
            {
                if ((E3.CurrentClass & Class.Priest) == E3.CurrentClass)
                {
                    returnValue = inputSetValue;
                }
            }
            if (inputs.Contains("Tanks", StringComparer.OrdinalIgnoreCase))
            {
                if ((E3.CurrentClass & Class.Tank) == E3.CurrentClass)
                {
                    returnValue = inputSetValue;
                }
            }
            if (inputs.Contains("Melee", StringComparer.OrdinalIgnoreCase))
            {
                if ((E3.CurrentClass & Class.Melee) == E3.CurrentClass)
                {
                    returnValue = inputSetValue;
                }
            }
            if (inputs.Contains("Casters", StringComparer.OrdinalIgnoreCase))
            {
                if ((E3.CurrentClass & Class.Caster) == E3.CurrentClass)
                {
                    returnValue = inputSetValue;
                }
            }
            if (inputs.Contains("Ranged", StringComparer.OrdinalIgnoreCase))
            {
                if ((E3.CurrentClass & Class.Ranged) == E3.CurrentClass)
                {
                    returnValue = inputSetValue;
                }
            }

            return !inputSetValue;
        }

        public static void StringsToNumbers(string s, char delim, List<Int32> list)
        {
            List<int> result = list;
            int start = 0;
            int end = 0;
            foreach (char x in s)
            {
                if (x == delim || end == s.Length - 1)
                {
                    if (end == s.Length - 1)
                        end++;
                    result.Add(int.Parse(s.Substring(start, end - start)));
                    start = end + 1;
                }
                end++;
            }

        }
        public static List<string> StringsToList(string s, char delim)
        {
            List<string> result = new List<string>();
            int start = 0;
            int end = 0;
            foreach (char x in s)
            {
                if (x == delim || end == s.Length - 1)
                {
                    if (end == s.Length - 1)
                        end++;
                    result.Add((s.Substring(start, end - start)));
                    start = end + 1;
                }
                end++;
            }

            return result;
        }
        public static void TryMoveToLoc(Double x, Double y,Double z, Int32 minDistance = 0,Int32 timeoutInMS = 10000 )
        {
            //Check for Nav path if available and Nav is loaded
            bool navLoaded = MQ.Query<bool>("${Plugin[MQ2Nav].IsLoaded}");
            int targetID = MQ.Query<int>("${Target.ID}");

            if (navLoaded)
            {
                bool meshLoaded = MQ.Query<bool>("${Navigation.MeshLoaded}");

                if (meshLoaded)
                {
                    NavToLoc(x,y,z);
                    //exit from TryMoveToLoc if we've reached the destination
                    Double distanceX = Math.Abs(x - MQ.Query<Double>("${Me.X}"));
                    Double distanceY = Math.Abs(y - MQ.Query<Double>("${Me.Y}"));

                    if (distanceX < 20 && distanceY < 20)
                    {
                        return;
                    }
                }
            }

            Double meX = MQ.Query<Double>("${Me.X}");
            Double meY = MQ.Query<Double>("${Me.Y}");
            MQ.Cmd($"/squelch /moveto loc {y} {x} mdist {minDistance}");
            if (timeoutInMS == -1) return;
            Int64 endTime = Core.StopWatch.ElapsedMilliseconds + timeoutInMS;
            MQ.Delay(300);
            while (true)
            {
                Double tmeX = MQ.Query<Double>("${Me.X}");
                Double tmeY = MQ.Query<Double>("${Me.Y}");

                if ((int)meX == (int)tmeX && (int)meY == (int)tmeY)
                {
                    //we are stuck, kick out
                    break;
                }

                meX = tmeX;
                meY = tmeY;

                if (endTime < Core.StopWatch.ElapsedMilliseconds)
                {
                    MQ.Cmd($"/squelch /moveto off");
                    break;
                }

                MQ.Delay(200);
            }


        }

        public static void PrintTimerStatus(Dictionary<Int32, SpellTimer> timers, ref Int64 printTimer, string Caption, Int64 delayInMS = 10000)
        {
            //Printing out debuff timers
            if (printTimer < Core.StopWatch.ElapsedMilliseconds)
            {
                if (timers.Count > 0)
                {
                    MQ.Write($"\at{Caption}");
                    MQ.Write("\aw===================");


                }

                foreach (var kvp in timers)
                {
                    foreach (var kvp2 in kvp.Value.Timestamps)
                    {
                        Data.Spell spell;
                        if (Spell._loadedSpells.TryGetValue(kvp2.Key, out spell))
                        {
                            Spawn s;
                            if (_spawns.TryByID(kvp.Value.MobID, out s))
                            {
                                MQ.Write($"\ap{s.CleanName} \aw: \ag{spell.CastName} \aw: {(kvp2.Value - Core.StopWatch.ElapsedMilliseconds) / 1000} seconds");

                            }

                        }
                        else
                        {
                            Spawn s;
                            if (_spawns.TryByID(kvp.Value.MobID, out s))
                            {
                                MQ.Write($"\ap{s.CleanName} \aw: \agspellid:{kvp2.Key} \aw: {(kvp2.Value - Core.StopWatch.ElapsedMilliseconds) / 1000} seconds");

                            }

                        }

                    }
                }
                if (timers.Count > 0)
                {
                    MQ.Write("\aw===================");

                }
                printTimer = Core.StopWatch.ElapsedMilliseconds + delayInMS;

            }
        }
        public static bool ClearCursor()
        {
            Int32 cursorID = MQ.Query<Int32>("${Cursor.ID}");
            Int32 counter = 0;
            while(cursorID>0)
            {   
                if (cursorID > 0)
                {
                    string autoinvItem = MQ.Query<string>("${Cursor}");
                    MQ.Cmd("/autoinventory");
                    if(autoinvItem!="NULL")
                    {
                        E3.Bots.Broadcast($"\agAutoInventory\aw:\ao{autoinvItem}");
                    }
                    MQ.Delay(300);
                }
                cursorID = MQ.Query<Int32>("${Cursor.ID}");
                if (counter > 5) break;
                counter++;
            }
            if (cursorID > 0) return false;
            return true;


        }
        public static void DeleteNoRentItem(string itemName)
        {
            if(ClearCursor())
            {
                bool foundItem = MQ.Query<bool>($"${{Bool[${{FindItem[={itemName}]}}]}}");
                if (!foundItem) return;
                MQ.Cmd($"/nomodkey /itemnotify \"{itemName}\" leftmouseup");
                MQ.Delay(2000, "${Bool[${Cursor.ID}]}");
                bool itemOnCursor = MQ.Query<bool>("${Bool[${Cursor.ID}]}");
                if(itemOnCursor)
                {
                    bool isNoRent = MQ.Query<bool>("${Cursor.NoRent}");
                    if(isNoRent)
                    {
                        MQ.Cmd("/destroy");
                        MQ.Delay(300);                        
                    }
                    ClearCursor();
                }
            }
        }
        public static void GiveItemOnCursorToTarget(bool moveBackToOriginalLocation = true, bool clearTarget = true)
        {

            double currentX = MQ.Query<double>("${Me.X}");
            double currentY = MQ.Query<double>("${Me.Y}");
            double currentZ = MQ.Query<double>("${Me.Z}");
            TryMoveToTarget();
            MQ.Cmd("/click left target");
            var targetType = MQ.Query<string>("${Target.Type}");
            var windowType = string.Equals(targetType, "PC", StringComparison.OrdinalIgnoreCase) ? "TradeWnd" : "GiveWnd";
            var buttonType = string.Equals(targetType, "PC", StringComparison.OrdinalIgnoreCase) ? "TRDW_Trade_Button" : "GVW_Give_Button";
            var windowOpenQuery = $"${{Window[{windowType}].Open}}";
            MQ.Delay(3000, windowOpenQuery);
            bool windowOpen = MQ.Query<bool>(windowOpenQuery);
            if(!windowOpen)
            {
                MQ.Write("\arError could not give target what is on our cursor, putting it in inventory");
                E3.Bots.BroadcastCommand($"/popup ${{Me}} cannot give ${{Cursor.Name}} to ${{Target}}", false);
                MQ.Cmd("/beep");
                MQ.Delay(100);
                MQ.Cmd("/autoinv");
                return;
            }
            Int32 waitCounter = 0;
            waitAcceptLoop:
            var command = $"/nomodkey /notify {windowType} {buttonType} leftmouseup";
            MQ.Cmd(command);
            if(string.Equals(targetType, "PC", StringComparison.OrdinalIgnoreCase))
            {
                E3.Bots.Trade(MQ.Query<string>("${Target.CleanName}"));
            }
            MQ.Delay(1000, $"!{windowOpenQuery}");
            windowOpen = MQ.Query<bool>(windowOpenQuery);
            if(windowOpen)
            {
                waitCounter++;
                if(waitCounter<30)
                {
                    goto waitAcceptLoop;

                }
            }

            if (clearTarget)
            {
                MQ.Cmd("/nomodkey /keypress esc");
            }

            //lets go back to our location
            if (moveBackToOriginalLocation)
            {
                e3util.TryMoveToLoc(currentX, currentY,currentZ);
            }
        }
        public static bool IsShuttingDown()
        {
            if(EventProcessor.CommandList["/shutdown"].queuedEvents.Count > 0)
            {
                return true;
            }
            return false;
        }
        public static void YieldToEQ()
        {
            MQ.Delay(0);
        }
        public static void RegisterCommandWithTarget(string command, Action<int> FunctionToExecute)
        {
            EventProcessor.RegisterCommand(command, (x) =>
            {
                 Int32 mobid;
                if (x.args.Count > 0)
                {
                    if (Int32.TryParse(x.args[0], out mobid))
                    {
                        if(_spawns.TryByID(mobid, out var spawn))
                        {
                            if(spawn.TypeDesc=="NPC")
                            {
                                FunctionToExecute(mobid);

                            }
                        }
                    }
                    else
                    {
                        MQ.Write($"\aNeed a valid target to {command}.");
                    }
                }
                else
                {
                    Int32 targetID = MQ.Query<Int32>("${Target.ID}");
                    if (targetID > 0)
                    {
                        if (_spawns.TryByID(targetID, out var spawn))
                        {
                            if (spawn.TypeDesc == "NPC")
                            {
                                //we are telling people to follow us
                                E3.Bots.BroadcastCommandToGroup($"{command} {targetID}");
                                FunctionToExecute(targetID);
                            }
                        }
                    }
                    else
                    {
                        MQ.Write($"\aNEED A TARGET TO {command}");
                    }
                }
            });

        }

        /// <summary>
        /// Picks up an item via the find item tlo.
        /// </summary>
        /// <param name="itemName">Name of the item.</param>
        /// <returns>a bool indicating whether the pickup was successful</returns>
        public static bool PickUpItemViaFindItemTlo(string itemName)
        {
            MQ.Cmd($"/nomodkey /itemnotify \"${{FindItem[={itemName}]}}\" leftmouseup");
            MQ.Delay(1000, "${Cursor.ID}");
            return MQ.Query<bool>("${Cursor.ID}");
        }

        /// <summary>
        /// Picks up an item via the inventory tlo.
        /// </summary>
        /// <param name="slotName">Name of the item.</param>
        /// <returns>a bool indicating whether the pickup was successful</returns>
        public static bool PickUpItemViaInventoryTlo(string slotName)
        {
            MQ.Cmd($"/nomodkey /itemnotify \"${{Me.Inventory[{slotName}]}}\" leftmouseup");
            MQ.Delay(1000, "${Cursor.ID}");
            return MQ.Query<bool>("${Cursor.ID}");
        }

        /// <summary>
        /// Clicks yes or no on a dialog box.
        /// </summary>
        /// <param name="YesClick">if set to <c>true</c> [yes click].</param>
        public static void ClickYesNo(bool YesClick)
        {
            string TypeToClick = "Yes";
            if (!YesClick)
            {
                TypeToClick = "No";
            }

            bool windowOpen = MQ.Query<bool>("${Window[ConfirmationDialogBox].Open}");
            if (windowOpen)
            {
                MQ.Cmd($"/nomodkey /notify ConfirmationDialogBox {TypeToClick}_Button leftmouseup");
            }
            else
            {
                windowOpen = MQ.Query<bool>("${Window[LargeDialogWindow].Open}");
                if (windowOpen)
                {
                    MQ.Cmd($"/nomodkey /notify LargeDialogWindow LDW_{TypeToClick}Button leftmouseup");
                }
            }
        }
        public static string FormatServerName(string serverName)
        {

            if (string.IsNullOrWhiteSpace(serverName)) return "Lazarus";

            if (serverName.Equals("Project Lazarus"))
            {
                return "Lazarus";
            }

            return serverName.Replace(" ", "_");
        }
        public static FileIniDataParser CreateIniParser()
        {
            var fileIniData = new FileIniDataParser();
            fileIniData.Parser.Configuration.AllowDuplicateKeys = true;
            fileIniData.Parser.Configuration.OverrideDuplicateKeys = true;// so that the other ones will be put into a collection
            fileIniData.Parser.Configuration.AssigmentSpacer = "";
            fileIniData.Parser.Configuration.CaseInsensitive = true;
           
            return fileIniData;
        }
        /// <summary>
        /// NavToSpawnID - use MQ2Nav to reach the specified spawn, right now just by ID, ideally by any valid nav command
        /// </summary>
        /// <param name="spawnID"></param>
        public static void NavToSpawnID(int spawnID, Int32 stopDistance=-1)
        {
            bool navPathExists = MQ.Query<bool>($"${{Navigation.PathExists[id {spawnID}]}}");
            bool navActive = MQ.Query<bool>("${Navigation.Active}");

            double minDistanceToChase = E3.GeneralSettings.Movement_ChaseDistanceMin;
            double maxDistanceToChase = E3.GeneralSettings.Movement_ChaseDistanceMax;

            //if a specific stop distance isn't set, use the NavStopDistance from general settings
            if(stopDistance == -1)
            {
                stopDistance = E3.GeneralSettings.Movement_NavStopDistance;
            }

            if (!navPathExists)
            {
                //early return if no path available
                MQ.Write($"\arNo nav path available to spawn ID: {spawnID}");
                return;
            }

            int timeoutInMS = 3000;

            
            MQ.Cmd($"/nav id {spawnID} distance={stopDistance}");
            
            Int64 endTime = Core.StopWatch.ElapsedMilliseconds + timeoutInMS;
            MQ.Delay(300);

            while (navPathExists && MQ.Query<int>("${Navigation.Velocity}") > 0)
            {
                Double meX = MQ.Query<Double>("${Me.X}");
                Double meY = MQ.Query<Double>("${Me.Y}");

                Double navPathLength = MQ.Query<Double>($"${{Navigation.PathLength[id {spawnID}]}}");

                if (Movement.Following && navPathLength > maxDistanceToChase)
                {
                    //Stop nav and break follow because distance is longer than max distance allowed
                    MQ.Cmd("/nav stop");
                    E3.Bots.Broadcast("${Me} stopping Nav because the path distance is greater than Chase Max distance.");
                    //Movement.Following = false;
                    break;
                }
                
                if (endTime < Core.StopWatch.ElapsedMilliseconds)
                {
                    //stop nav if we exceed the timeout
                    MQ.Write("Stopping because timeout exceeded for navigation");
                    MQ.Cmd($"/nav stop");
                    break;
                }
                MQ.Delay(1000);
                
                navActive = MQ.Query<bool>("${Navigation.Active}");
                if (!navActive)
                {
                    //kick out if Nav ended during delay
                    break;
                }

                Double tmeX = MQ.Query<Double>("${Me.X}");
                Double tmeY = MQ.Query<Double>("${Me.Y}");
                
                if ((int)meX == (int)tmeX && (int)meY == (int)tmeY)
                {
                    //we are stuck, kick out
                    E3.Bots.Broadcast("${Me} stopping Nav because we appear to be stuck.");
                    MQ.Cmd($"/nav stop");
                    break;
                }
                //add additional time to get to target
                endTime += timeoutInMS;
                navPathExists = MQ.Query<bool>($"${{Navigation.PathExists[id {spawnID}]}}");
            }
        }

        private static void NavToLoc(Double locX, Double locY, Double locZ)
        {
            bool navActive = MQ.Query<bool>("${Navigation.Active}");
            var navQuery = $"locxyz {locX} {locY} {locZ}";

            var navPathExists = MQ.Query<bool>($"${{Navigation.PathExists[{navQuery}]}}");
            
            if (!navPathExists)
            {
                //early return if no path available
                var message = $"\arNo navpath available to location X:{locX} Y:{locY}";
                if (locZ > -1)
                {
                    message += $" Z:{locZ}";
                }

                MQ.Write(message);
                return;
            }

            int timeoutInMS = 3000;

            MQ.Cmd($"/nav {navQuery}");
            

            Int64 endTime = Core.StopWatch.ElapsedMilliseconds + timeoutInMS;
            MQ.Delay(300);

            while (navPathExists && MQ.Query<int>("${Navigation.Velocity}") > 0)
            {
                Double meX = MQ.Query<Double>("${Me.X}");
                Double meY = MQ.Query<Double>("${Me.Y}");

                if (endTime < Core.StopWatch.ElapsedMilliseconds)
                {
                    //stop nav if we exceed the timeout
                    MQ.Write("Stopping because timeout exceeded for navigation");
                    MQ.Cmd($"/nav stop");
                    break;
                }
                MQ.Delay(1000);

                navActive = MQ.Query<bool>("${Navigation.Active}");
                if (!navActive)
                {
                    //kick out if Nav ended during delay
                    break;
                }

                Double tmeX = MQ.Query<Double>("${Me.X}");
                Double tmeY = MQ.Query<Double>("${Me.Y}");

                if ((int)meX == (int)tmeX && (int)meY == (int)tmeY)
                {
                    //we are stuck, kick out
                    E3.Bots.Broadcast("${Me} stopping Nav because we appear to be stuck.");
                    MQ.Cmd($"/nav stop");
                    break;
                }
                //add additional time to get to target
                endTime += timeoutInMS;

                navPathExists = MQ.Query<bool>($"${{Navigation.PathExists[{navQuery}]}}");
            }
        }
        

        public static void OpenMerchant()
        {
            e3util.TryMoveToTarget();
            MQ.Cmd("/click right target");
            MQ.Delay(2000, "${Merchant.ItemsReceived}");
        }
        public static void CloseMerchant()
        {
            bool merchantWindowOpen = MQ.Query<bool>("${Window[MerchantWnd].Open}");
            
            MQ.Cmd("/nomodkey /notify MerchantWnd MW_Done_Button leftmouseup");
            MQ.Delay(200);
        }

        public static bool ValidateCursor(int expected)
        {
            var cursorId = MQ.Query<int>("${Cursor.ID}");
            if (cursorId == -1)
            {
                E3.Bots.Broadcast("\arError: Nothing on cursor when we expected something.");
            }

            return expected == cursorId;
        }

        public static bool IsActionBlockingWindowOpen()
        {
            var vendorOpen = MQ.Query<bool>("${Window[MerchantWnd]}");
            var bankOpen = MQ.Query<bool>("${Window[BigBankWnd]}");
            var guildBankOpen = MQ.Query<bool>("${Window[GuildBankWnd]}");
            var tradeOpen = MQ.Query<bool>("${Window[TradeWnd]}");
            var giveOpen = MQ.Query<bool>("${Window[GiveWnd]}");

            return (vendorOpen || bankOpen || guildBankOpen || tradeOpen || giveOpen);
        }

        public static void Exchange(string slotName, string itemName)
        {
            MQ.Cmd($"/exchange \"{itemName}\" \"{slotName}\"");
        }
        public static string FirstCharToUpper(string input)
        {
            switch (input)
            {
                case null: return null;
                case "": return String.Empty;
                default: return input[0].ToString().ToUpper() + input.Substring(1);
            }
        }
    }
}
