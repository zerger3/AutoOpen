﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AutoOpen.Utils;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;

namespace AutoOpen
{
    public class AutoOpen : BaseSettingsPlugin<Settings>
    {
        private IngameState ingameState;
        private readonly Dictionary<long, int> clickedEntities = new Dictionary<long, int>();
        private Vector2 windowOffset;
        private List<string> doorBlacklist;
        private List<string> switchBlacklist;
        private List<string> chestWhitelist;

        public override bool Initialise()
        {
            base.Initialise();
            Name = "AutoOpen";

            ingameState = GameController.Game.IngameState;
            windowOffset = GameController.Window.GetWindowRectangle().TopLeft;
            loadDoorBlacklist();
            loadSwitchBlacklist();
            loadChestWhitelist();
            return true;
        }

        public override void Render()
        {
            if (!Settings.Enable) return;
            open();
        }

        private void open()
        {
            var camera = ingameState.Camera;
            var playerPos = GameController.Player.Pos;
            var prevMousePosition = Mouse.GetCursorPosition();

            var entities = GameController.Entities
                .Where(entity => entity.HasComponent<Render>() &&
                                 entity.Address != GameController.Player.Address &&
                                 entity.IsValid &&
                                 entity.IsTargetable &&
                                 (entity.HasComponent<TriggerableBlockage>() ||
                                  entity.HasComponent<Transitionable>() ||
                                  entity.HasComponent<Chest>() ||
                                  entity.HasComponent<Shrine>() ||
                                  entity.Path.ToLower().Contains("darkshrine")));
            
            foreach (var entity in entities)
            {
                var entityPos = entity.Pos;
                var entityScreenPos = camera.WorldToScreen(entityPos.Translate(0, 0, 0));
                var entityDistanceToPlayer =
                    Math.Sqrt(Math.Pow(playerPos.X - entityPos.X, 2) + Math.Pow(playerPos.Y - entityPos.Y, 2));

                var isTargetable = entity.GetComponent<Targetable>().isTargetable;
                var isTargeted = entity.GetComponent<Targetable>().isTargeted;

                //Doors
                if (Settings.doors)
                {
                    var isBlacklisted = doorBlacklist != null && doorBlacklist.Contains(entity.Path);


                    if (entity.HasComponent<TriggerableBlockage>() && entity.HasComponent<Targetable>() &&
                        
                         (
                          entity.Path.ToLower().Contains("door") || entity.Path.ToLower().Contains("door_basic") && !entity.Path.ToLower().Contains("npclockpicker") 
                         )
                        
                        )
                    {
                        var isClosed = entity.GetComponent<TriggerableBlockage>().IsClosed;

                        var s = isClosed ? "closed" : "opened";
                        var c = isClosed ? Color.Red : Color.Green;

                        if (!isBlacklisted) Graphics.DrawText(s, entityScreenPos, c, FontAlign.Center);

                        if (isTargeted)
                            if (Keyboard.IsKeyPressed((int) Settings.toggleEntityKey.Value))
                                toggleDoorBlacklistItem(entity.Path);

                        if (Control.MouseButtons == MouseButtons.Left)
                        {
                            var clickCount = getEntityClickedCount(entity);
                            if (!isBlacklisted && entityDistanceToPlayer <= Settings.doorDistance && isClosed &&
                                clickCount <= 15)
                            {
                                open(entityScreenPos, prevMousePosition);
                                clickedEntities[entity.Address] = clickCount + 1;
                                if (Settings.BlockInput) Mouse.blockInput(true);
                            }
                            else if (!isBlacklisted && entityDistanceToPlayer >= Settings.doorDistance &&
                                     isClosed && clickCount >= 15)
                            {
                                clickedEntities.Clear();
                            }

                            if (Settings.BlockInput) Mouse.blockInput(false);
                        }
                    }
                }

                //Switches
                if (Settings.switches)
                {
                    var isBlacklisted = switchBlacklist != null && switchBlacklist.Contains(entity.Path);

                    if (entity.HasComponent<Transitionable>() && entity.HasComponent<Targetable>() &&
                        !entity.HasComponent<TriggerableBlockage>() && entity.Path.ToLower().Contains("switch"))
                    {
                        var switchState = entity.GetComponent<Transitionable>().Flag1;
                        var switched = switchState != 1;

                        var s = isTargeted ? "targeted" : "not targeted";
                        var c = isTargeted ? Color.Green : Color.Red;

                        if (!isBlacklisted)
                        {
                            var count = 1;
                            Graphics.DrawText(s, entityScreenPos.Translate(0, count * 16), c, FontAlign.Center);
                            count++;
                            var s2 = switched ? "switched" : "not switched";
                            var c2 = switched ? Color.Green : Color.Red;
                            Graphics.DrawText(s2 + ":" + switchState, entityScreenPos.Translate(0, count * 16), c2,
                                FontAlign.Center);
                            count++;
                        }

                        if (isTargeted)
                            if (Keyboard.IsKeyPressed((int) Settings.toggleEntityKey.Value))
                                toggleSwitchBlacklistItem(entity.Path);

                        if (Control.MouseButtons == MouseButtons.Left)
                        {
                            var clickCount = getEntityClickedCount(entity);
                            if (!isBlacklisted && entityDistanceToPlayer <= Settings.switchDistance && !switched &&
                                clickCount <= 15)
                            {
                                open(entityScreenPos, prevMousePosition);
                                clickedEntities[entity.Address] = clickCount + 1;
                                if (Settings.BlockInput) Mouse.blockInput(true);
                            }
                            else if (!isBlacklisted && entityDistanceToPlayer >= Settings.switchDistance &&
                                     !switched && clickCount >= 15)
                            {
                                clickedEntities.Clear();
                            }

                            if (Settings.BlockInput) Mouse.blockInput(false);
                        }
                    }
                }

                //Chests
                if (Settings.chests)
                    if (entity.HasComponent<Chest>() || entity.Path.ToLower().Contains("chest"))
                    {
                        var isOpened = entity.GetComponent<Chest>().IsOpened;
                        var whitelisted = chestWhitelist != null && chestWhitelist.Contains(entity.Path);

                        if (isTargetable && !isOpened && whitelisted)
                            Graphics.DrawText("Open me!", entityScreenPos, Color.LimeGreen, FontAlign.Center);

                        if (isTargeted)
                            if (Keyboard.IsKeyPressed((int) Settings.toggleEntityKey.Value))
                                toggleChestWhitelistItem(entity.Path);

                        if (Control.MouseButtons == MouseButtons.Left)
                        {
                            var clickCount = getEntityClickedCount(entity);

                            if (isTargetable && whitelisted && entityDistanceToPlayer <= Settings.chestDistance &&
                                !isOpened && clickCount <= 15)
                            {
                                open(entityScreenPos, prevMousePosition);
                                clickedEntities[entity.Address] = clickCount + 1;
                                if (Settings.BlockInput) Mouse.blockInput(true);
                            }
                            else if (isTargetable && whitelisted &&
                                     entityDistanceToPlayer >= Settings.chestDistance && !isOpened &&
                                     clickCount >= 15)
                            {
                                clickedEntities.Clear();
                            }

                            if (Settings.BlockInput) Mouse.blockInput(false);
                        }
                    }

                //Shrines
                if (Settings.shrines)
                    if (entity.HasComponent<Shrine>() || entity.Path.ToLower().Contains("darkshrine"))
                    {
                        var isAvailable = entity.GetComponent<Shrine>().IsAvailable;
                        var whitelisted = chestWhitelist.Contains(entity.Path);

                        if (isTargetable)
                            Graphics.DrawText("Get me!", entityScreenPos, Color.LimeGreen, FontAlign.Center);

                        if (Control.MouseButtons == MouseButtons.Left)
                        {
                            var clickCount = getEntityClickedCount(entity);

                            if (isTargetable && entityDistanceToPlayer <= Settings.shrineDistance &&
                                clickCount <= 15)
                            {
                                open(entityScreenPos, prevMousePosition);
                                clickedEntities[entity.Address] = clickCount + 1;
                                if (Settings.BlockInput) Mouse.blockInput(true);
                            }
                            else if (isTargetable && entityDistanceToPlayer >= Settings.shrineDistance &&
                                     clickCount >= 15)
                            {
                                clickedEntities.Clear();
                            }

                            if (Settings.BlockInput) Mouse.blockInput(false);
                        }
                    }
            }
        }

        private int getEntityClickedCount(Entity entity)
        {
            var clickCount = 0;
            if (clickedEntities.ContainsKey(entity.Address))
                clickCount = clickedEntities[entity.Address];
            else
                clickedEntities.Add(entity.Address, clickCount);
            if (clickCount >= 15) LogMessage(entity.Path + " clicked too often!", 3);
            return clickCount;
        }

        private void open(Vector2 entityScreenPos, Vector2 prevMousePosition)
        {
            entityScreenPos += windowOffset;
            Mouse.moveMouse(entityScreenPos);
            Mouse.LeftUp(0);
            Mouse.LeftDown(0);
            Mouse.LeftUp(0);
            Mouse.moveMouse(prevMousePosition);
            Mouse.LeftDown(0);
            Thread.Sleep(Settings.Speed);
        }

        private void loadDoorBlacklist()
        {
            try
            {
                doorBlacklist = File.ReadAllLines(DirectoryFullName + "\\doorBlacklist.txt").ToList();
            }
            catch (Exception)
            {
                File.Create(DirectoryFullName + "\\doorBlacklist.txt");
                loadDoorBlacklist();
            }
        }

        private void loadSwitchBlacklist()
        {
            try
            {
                switchBlacklist = File.ReadAllLines(DirectoryFullName + "\\switchBlacklist.txt").ToList();
            }
            catch (Exception)
            {
                File.Create(DirectoryFullName + "\\switchBlacklist.txt");
                loadSwitchBlacklist();
            }
        }

        private void loadChestWhitelist()
        {
            try
            {
                chestWhitelist = File.ReadAllLines(DirectoryFullName + "\\chestWhitelist.txt").ToList();
            }
            catch (Exception)
            {
                File.Create(DirectoryFullName + "\\chestWhitelist.txt");
                loadChestWhitelist();
            }
        }

        private void toggleDoorBlacklistItem(string name)
        {
            if (doorBlacklist.Contains(name))
            {
                doorBlacklist.Remove(name);
                LogMessage(name + " will now be opened", 5, Color.Green);
            }
            else
            {
                doorBlacklist.Add(name);
                LogMessage(name + " will now be ignored", 5, Color.Red);
            }

            File.WriteAllLines(DirectoryFullName + "\\doorBlacklist.txt", doorBlacklist);
        }

        private void toggleSwitchBlacklistItem(string name)
        {
            if (switchBlacklist.Contains(name))
            {
                switchBlacklist.Remove(name);
                LogMessage(name + " will now be opened", 5, Color.Green);
            }
            else
            {
                switchBlacklist.Add(name);
                LogMessage(name + " will now be ignored", 5, Color.Red);
            }

            File.WriteAllLines(DirectoryFullName + "\\switchBlacklist.txt", switchBlacklist);
        }

        private void toggleChestWhitelistItem(string name)
        {
            if (chestWhitelist.Contains(name))
            {
                chestWhitelist.Remove(name);
                LogMessage(name + " will now be ignored", 5, Color.Red);
            }
            else
            {
                chestWhitelist.Add(name);
                LogMessage(name + " will now be opened", 5, Color.Green);
            }

            File.WriteAllLines(DirectoryFullName + "\\chestWhitelist.txt", chestWhitelist);
        }

        public override void AreaChange(AreaInstance area)
        {
            clickedEntities.Clear();
            base.AreaChange(area);
        }
    }
}