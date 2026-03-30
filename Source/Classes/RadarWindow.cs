using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Policy;
using ImGuiNET;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using PixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace DMAW_DND
{
    public unsafe class RadarWindow : GameWindow
    {
        public ImGuiController _controller;
        private bool _initStyles = false;
        private bool _borderLess = false;
        private ImFontPtr _font;

        private bool enableDeveloper = false;
        private bool developerMapBypass = true;
        private int debugDeveloper = 1;
        private int settingsTab = 0;

        public static Vector2 lastPosition = new Vector2(0f, 0f);
        public static Vector2 position = new Vector2(0f, 0f);
        private double frameTime;
        public static Vector2 renderWindowPos = new Vector2(0f, 0f);
        public static Vector2 renderWindowSize = new Vector2(0f, 0f);

        // C4 RING STUFF
        private static float ringRadius = 30.0f;
        private static float ringThickness = 3.0f;
        private static float pulsationSpeed = 10.0f;

        // MAP STUFF
        private Vector2 positionCurrent = new Vector2(0f, 0f);
        public static Vector2 imageSize = new Vector2(1000f, 1000f);
        private Vector2 imageSizeCurrent = new Vector2(1000f, 1000f);
        private float sliderMapX = 1f;
        private float sliderMapY = 1f;
        private float sliderMapScale = 1f;
        private static Vector2 textureSize = new Vector2(0f, 0f);
        private float factor = 1.2f;
        List<(Vector2 Position, float Radius, Player PlayerData)> playerPills = new List<(Vector2, float, Player)>();

        private nint RenderTexture = 0;

        private readonly object _renderLock = new();

        private static Map _selectedMap = null;
        private static string _lastHandledGameMapStrip = "";
        private static List<Map> _maps = new List<Map>();
        private int mapHeightIndex;
        private int _mapSelectionIndex;
        private static IntPtr renderTexture = (IntPtr)0;
        public static nint[] _loadedBitmaps;
        public static bool mapsLoaded = false;

        // SETTINGS
        private bool _isFreeMode = false;
        private bool _showRadarSettings = false;

        private DateTime _lastRadarDiagUtc = DateTime.MinValue;

        /// <summary>Reused every frame — avoids allocating <see cref="StopWatchMilliseconds"/> per frame.</summary>
        private readonly Stopwatch _radarFrameSw = new Stopwatch();
        /// <summary>Phase for ring pulse; driven by frame time instead of <see cref="DateTime.Now"/> (allocations).</summary>
        private float _pulsePhase;

        public RadarWindow() : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new OpenTK.Mathematics.Vector2i(1920, 1080),
            APIVersion = new Version(4, 4),
            WindowState = WindowState.Maximized
        })
        { }

        protected unsafe override void OnLoad()
        {
            ActivityLog.Info("RadarWindow", "OnLoad: begin (loading maps)");
            while(!mapsLoaded)
            {
                // Load Maps
                mapsLoaded = LoadMaps();
            }

            base.OnLoad();
            base.Title = "DMAWarehouse Radar V2";
            base.VSync = VSyncMode.Off;
            this._controller = new ImGuiController(base.ClientSize.X, base.ClientSize.Y);

            _font = ImGui.GetFont();

            ActivityLog.Info("RadarWindow", $"OnLoad: complete ClientSize={base.ClientSize.X}x{base.ClientSize.Y}");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ActivityLog.Info("RadarWindow", "OnClosing: window closing");
            // Could probably kill memory reads here
            DisposeOldTextures();
            //Config.SaveConfig(this._config);
            Process.GetCurrentProcess().Kill();
            base.OnClosing(e);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            ActivityLog.Info("RadarWindow", $"OnResize: {e.Width}x{e.Height}");
            base.OnResize(e);
            GL.Viewport(0, 0, base.ClientSize.X, base.ClientSize.Y);
            if(this._controller != null)
            {
                this._controller.WindowResized(base.ClientSize.X, base.ClientSize.Y);
            }
        }

        protected unsafe override void OnRenderFrame(FrameEventArgs e)
        {
            this.Context.MakeCurrent();
            playerPills.Clear();
            _radarFrameSw.Restart();
            _pulsePhase += (float)e.Time * pulsationSpeed;
            ringRadius = 30.0f + 20.0f * MathF.Sin(_pulsePhase);
            this.HandleMapChange();
            //UpdateMapPosition();
            if (_isFreeMode)
            {
                this.positionCurrent.X = this.Interpolate(this.positionCurrent.X, position.X, (float)(this.frameTime / 75f));
                this.positionCurrent.Y = this.Interpolate(this.positionCurrent.Y, position.Y, (float)(this.frameTime / 75f));
            }
            else
            {
                // In free mode, directly use the position set by mouse movement
                this.positionCurrent.X = this.Interpolate(this.positionCurrent.X, position.X, (float)(this.frameTime / 75f));
                this.positionCurrent.Y = this.Interpolate(this.positionCurrent.Y, position.Y, (float)(this.frameTime / 75f));
                //this.positionCurrent = position;
            }
            base.OnRenderFrame(e);
            this._controller.Update(this, (float)e.Time);
            GL.ClearColor(new OpenTK.Mathematics.Color4(0, 0, 0, byte.MaxValue));
            GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit | ClearBufferMask.ColorBufferBit);
            this.RenderMain();
            this.CheckForHoverAndShowTooltip();
            this._controller.Render();
            ImGuiController.CheckGLError("End of frame");
            this.Context.SwapBuffers();
            this.frameTime = _radarFrameSw.Elapsed.TotalMilliseconds;
            if (ActivityLog.LogEveryFrame)
                ActivityLog.Trace("RadarWindow", $"OnRenderFrame: frameTimeMs={this.frameTime:F2}");
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            ActivityLog.Info("UI", $"OnTextInput: Unicode=U+{e.Unicode:X4}");
            base.OnTextInput(e);
            this._controller.PressChar((char)e.Unicode);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            ActivityLog.Info("UI", $"OnMouseWheel: Offset=({e.OffsetX:F3},{e.OffsetY:F3})");
            base.OnMouseWheel(e);
            Vector2 vector = ImGui.GetIO().DisplaySize / 2f;

            if (e.OffsetY > 0f)
            {
                if ((imageSize.X / textureSize.X) > 10f)
                {
                    return;
                }

                Vector2 originalImageSize = new Vector2(imageSize.X, imageSize.Y);

                imageSize *= factor;
                position.X -= (imageSize.X - originalImageSize.X) * (vector.X - position.X) / originalImageSize.X;
                position.Y -= (imageSize.Y - originalImageSize.Y) * (vector.Y - position.Y) / originalImageSize.Y;
            }
            else
            {
                if ((imageSize.X / textureSize.X) < 0.1f)
                {
                    return;
                }

                Vector2 originalImageSize = new Vector2(imageSize.X, imageSize.Y);

                imageSize /= factor;
                position.X -= (imageSize.X - originalImageSize.X) * (vector.X - position.X) / originalImageSize.X;
                position.Y -= (imageSize.Y - originalImageSize.Y) * (vector.Y - position.Y) / originalImageSize.Y;
            }
            this._controller.MouseScroll(e.Offset);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            ActivityLog.Info("UI", $"OnMouseDown: {e.Button} position=({MouseState.X},{MouseState.Y})");
            base.OnMouseDown(e);

            bool mouseInModal = ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
            if (_isFreeMode && e.Button == MouseButton.Left && !mouseInModal)
            {
                var mouseState = MouseState;
                lastPosition = new Vector2(mouseState.X, mouseState.Y);
                //Program.Log("MouseDown - LastPosition set: " + lastPosition);
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            ActivityLog.Info("UI", $"OnMouseUp: {e.Button} position=({MouseState.X},{MouseState.Y})");
            base.OnMouseUp(e);
            if (_isFreeMode && e.Button == MouseButton.Left)
            {
                lastPosition = Vector2.Zero;
            }
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            if (ActivityLog.LogMouseMoves)
                ActivityLog.Trace("UI", $"OnMouseMove: ({e.X},{e.Y}) delta=({e.DeltaX},{e.DeltaY})");
            base.OnMouseMove(e);
            if (_isFreeMode && lastPosition != Vector2.Zero)
            {
                Vector2 currentPosition = new Vector2((float)e.X, (float)e.Y);
                Vector2 delta = currentPosition - lastPosition;
                position.X += delta.X;
                position.Y += delta.Y;
                lastPosition = currentPosition;
                //Program.Log("MouseMove - Position updated: " + position);
            }
        }

        private void RenderMain()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if (!this._initStyles)
            {
                this._initStyles = this.InitStyle();
            }
            renderWindowSize = io.DisplaySize;

            ImDrawListPtr backgroundDrawList = ImGui.GetBackgroundDrawList();
            
            //if (!Memory.InGame && this.previousGameState)
            //{
            //    this.previousGameState = false;
            //}
            //if (Memory.InGame && !this.previousGameState)
            //{
            //    this.previousGameState = true;
            //}
            //backgroundDrawList.AddRectFilled(new Vector2(0f, 0f), io.DisplaySize, ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1f)));

            float num13 = 75f;
            if (this.frameTime > (double)num13)
            {
                this.frameTime = (double)num13;
            }
            else if (this.frameTime < 0.0)
            {
                this.frameTime = 0.0;
            }

            if (ActivityLog.RadarDiagnostics && (DateTime.UtcNow - _lastRadarDiagUtc).TotalSeconds >= 3)
            {
                _lastRadarDiagUtc = DateTime.UtcNow;
                var inGame = Memory.InGame;
                var gs = Memory.GameStatus;
                var draw = inGame && gs == Enums.GameStatus.InGame;
                var pl = Memory.Players;
                var pc = pl?.Count ?? 0;
                Player? local = null;
                if (pl != null)
                    foreach (var kv in pl)
                        if (kv.Value.Type == PlayerType.LocalPlayer) { local = kv.Value; break; }
                var items = EntityManager.Items?.Count ?? 0;
                var texOk = _loadedBitmaps != null && _loadedBitmaps.Length > 0 && _loadedBitmaps[0] != IntPtr.Zero;
                var cfg = Config.ActiveConfig;
                ActivityLog.Info("RadarDiag",
                    $"drawRadar={draw} Memory.InGame={inGame} GameStatus={gs} Players={pc} localPlayer={(local != null)} localXY=({local?.Location.X:F1},{local?.Location.Y:F1}) itemsPOI={items} ShowPOIs={cfg.ShowPOIs} ShowBosses={cfg.ShowBosses} ShowMobs={cfg.ShowMobs} map={_selectedMap?.Name} texturesOk={texOk} MemoryReady={Memory.Ready}");
                if (!draw)
                    ActivityLog.Warn("RadarDiag", "Not drawing map overlay: need Memory.InGame=true AND GameStatus=InGame. If stuck here while in dungeon, check memory thread / GameStatus.");
            }

            if (Memory.InGame && Memory.GameStatus == Enums.GameStatus.InGame)
            {
                this.RenderTexture = _loadedBitmaps[0];

                var playersSnap = Memory.Players;
                Player? LocalPlayer = null;
                if (playersSnap != null)
                {
                    foreach (var kv in playersSnap)
                    {
                        if (kv.Value.Type == PlayerType.LocalPlayer)
                        {
                            LocalPlayer = kv.Value;
                            break;
                        }
                    }
                }
                if (LocalPlayer != null && !_isFreeMode)
                {
                    PointCenterMap(LocalPlayer.Location);
                }

                //this.positionCurrent.X = this.Interpolate(this.positionCurrent.X, position.X, (float)(this.frameTime / (double)num13));
                //this.positionCurrent.Y = this.Interpolate(this.positionCurrent.Y, position.Y, (float)(this.frameTime / (double)num13));
                this.imageSizeCurrent.X = this.Interpolate(this.imageSizeCurrent.X, imageSize.X, (float)(this.frameTime / (double)num13));
                this.imageSizeCurrent.Y = this.Interpolate(this.imageSizeCurrent.Y, imageSize.Y, (float)(this.frameTime / (double)num13));

                // set this position to the center of the map
                //this.positionCurrent = new Vector2((io.DisplaySize.X / 2) - (imageSizeCurrent.X / 2), (io.DisplaySize.Y / 2) - (imageSizeCurrent.Y / 2));
                //this.imageSizeCurrent = new Vector2(1000f, 1000f);
                
                backgroundDrawList.AddImage(RenderTexture, this.positionCurrent, this.positionCurrent + this.imageSizeCurrent);
                if (Config.ActiveConfig.ShowPOIs)
                {
                    foreach (var item in EntityManager.Items)
                    {
                        //draw items
                        //var itemPos = GetMapPos(item.Value.ActorLocation, 1);
                        //if (item.Value.Name.Contains("Statue") || item.Value.Name.Contains("Altar"))
                        //{
                        //    backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(0, 0, 1, 1)));
                        //}
                        //else if (item.Value.Name.Contains("Chest"))
                        //{
                        //    backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0.8f, 0, 1)));
                        //}
                        //else if (item.Value.Name.Contains("Portal"))
                        //{
                        //    backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(0, 1, 1, 1)));
                        //}
                        //else if (item.Value.EnemyHealth > 0)
                        //{
                        //    backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
                        //    backgroundDrawList.AddText(itemPos + new Vector2(10, +10), ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), item.Value.EnemyHealth.ToString() + "HP");
                        //}
                        //else
                        //{
                        //    backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
                        //}
                        ////add item name
                        //backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);

                        var itemPos = new Vector2(0, 0);

                        switch(item.Value.Type)
                        {
                            case Enums.ActorType.Statue:
                                if (!Config.ActiveConfig.ShowShrines) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(0, 0, 1, 1)));
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);
                                break;
                            case Enums.ActorType.Portal:
                                if (!Config.ActiveConfig.ShowPortals) continue;
                                float heightDifference = LocalPlayer != null && LocalPlayer.Health > 0 ? ((float)LocalPlayer.CompToWorld.Translation.Z - (float)item.Value.ActorLocation.Z) * 0.08f : 0;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(0, 1, 1, 1)));
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);
                                //draw triangle to indicate height difference
                                int relativeHeightInt = heightDifference > 0 ? -1 : 1;
                                Vector2 point1 =
                                    relativeHeightInt > 0
                                        ? new Vector2(itemPos.X - 20, itemPos.Y - 10)
                                        : new Vector2(itemPos.X - 20, itemPos.Y + 10);
                                Vector2 point2 = new Vector2(itemPos.X - 15, itemPos.Y);
                                Vector2 point3 = new Vector2(itemPos.X - 25, itemPos.Y);
                                backgroundDrawList.AddTriangleFilled(point1, point2, point3, ImGui.GetColorU32(ImGuiCol.Text));
                                break;
                            case Enums.ActorType.Chest:
                                if (!Config.ActiveConfig.ShowChests) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0.8f, 0, 1)));
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);
                                break;
                            case Enums.ActorType.Lever:
                                if (!Config.ActiveConfig.ShowLevers) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);
                                break;
                            case Enums.ActorType.Ore:
                                if (!Config.ActiveConfig.ShowOre) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 1, 0, 1)));
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);
                                break;
                            case Enums.ActorType.Mimic:
                                if (!Config.ActiveConfig.ShowMimics) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);
                                break;
                            case Enums.ActorType.Special:
                                if (!Config.ActiveConfig.ShowSpecialItems) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), item.Value.Name);
                                break;
                            case Enums.ActorType.Boss:
                                if (!Config.ActiveConfig.ShowBosses) continue;
                                // Hide only when we successfully read HP and the boss is dead (same idea as players).
                                if (item.Value.EnemyMaxHealth.GetValueOrDefault() > 0 && item.Value.EnemyHealth.GetValueOrDefault(1) == 0) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 3f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
                                var label = $"{item.Value.Name} - {item.Value.EnemyHealth}";
                                backgroundDrawList.AddText(itemPos + new Vector2(9, 9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), label); // Black outline
                                backgroundDrawList.AddText(itemPos + new Vector2(10, 10), ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), label);
                                break;
                            case Enums.ActorType.NPC:
                                if (!Config.ActiveConfig.ShowMobs) continue;
                                if (item.Value.EnemyMaxHealth.GetValueOrDefault() > 0 && item.Value.EnemyHealth.GetValueOrDefault(1) == 0) continue;
                                itemPos = GetMapPos(item.Value.ActorLocation);
                                backgroundDrawList.AddCircleFilled(itemPos, 2.8f * Config.ActiveConfig.UIScale, ImGui.GetColorU32(new Vector4(1f, 0.45f, 0.08f, 1f)));
                                backgroundDrawList.AddText(itemPos + new Vector2(13, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), item.Value.Name);
                                backgroundDrawList.AddText(itemPos + new Vector2(14, -10), ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.65f, 1f)), item.Value.Name);
                                break;
                            default:
                                break;
                        }
                    }
                }
                if (LocalPlayer != null)
                {
                    var localPlayerPos = GetMapPos(LocalPlayer.Location, 1);
                    DrawPlayerPill(backgroundDrawList, LocalPlayer, (float)LocalPlayer.Rotation.Pitch - 90.0f, localPlayerPos, ImGui.GetColorU32(new Vector4(0, 1, 0, 1)), 0);
                }

                //render other players
                if (playersSnap != null)
                {
                    foreach (var player in playersSnap)
                    {
                        if (player.Value.Type == PlayerType.LocalPlayer)
                            continue;
                        // Hide only when we have a reliable max-health read and current HP is zero (skip filter if stats did not replicate).
                        if (player.Value.MaxHealth > 0 && player.Value.Health <= 0)
                            continue;
                        //get player
                        float heightDifference = LocalPlayer != null && LocalPlayer.Health > 0 ? ((float)LocalPlayer.CompToWorld.Translation.Z - (float)player.Value.CompToWorld.Translation.Z) * 0.08f : 0;
                        if(heightDifference < 1 && heightDifference > -1)
                        {
                            heightDifference = 0;
                        }
                        var playerPos = GetMapPos(player.Value.Location, 1);
                        var uniqueColor = generateTeamColor(player.Value.PartyID);
                        DrawPlayerPill(backgroundDrawList, player.Value, (float)player.Value.Rotation.Pitch - 90.0f, playerPos, ImGui.GetColorU32(uniqueColor), heightDifference);
                        //DrawPlayerPill(backgroundDrawList, (float)player.Value.Rotation.Pitch - 90.0f, playerPos, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), heightDifference);
                        ////add player name as white text with much larger black outline
                        //backgroundDrawList.AddText(playerPos + new Vector2(15, -10), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), player.Value.Name);
                        //backgroundDrawList.AddText(playerPos + new Vector2(15, -12), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), player.Value.Name);
                        //use font and font size
                        // Shadow - draw this first
                        backgroundDrawList.AddText(io.FontDefault, 20f, playerPos + new Vector2(16, -9), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), player.Value.Name);
                        backgroundDrawList.AddText(io.FontDefault, 20f, playerPos + new Vector2(15, -10), ImGui.GetColorU32(uniqueColor), player.Value.Name);
                        var classLabel = string.IsNullOrWhiteSpace(player.Value.Class) ? "Unknown" : player.Value.Class;
                        var hpLabel = player.Value.MaxHealth > 0
                            ? $"{MathF.Round(player.Value.Health)}/{MathF.Round(player.Value.MaxHealth)}HP"
                            : "HP ?";
                        backgroundDrawList.AddText(io.FontDefault, 20f, playerPos + new Vector2(16, -29), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), $"{classLabel} {hpLabel}");
                        backgroundDrawList.AddText(io.FontDefault, 20f, playerPos + new Vector2(15, -30), ImGui.GetColorU32(uniqueColor), $"{classLabel} {hpLabel}");

                    }
                }
                if (Config.ActiveConfig.ShowTeamLines && playersSnap != null)
                {
                    // One pass + dictionary: avoid per-frame LINQ (GroupBy/Where) allocations.
                    var byParty = new Dictionary<uint, List<KeyValuePair<int, Player>>>(8);
                    foreach (var p in playersSnap)
                    {
                        if (p.Value.Type == PlayerType.LocalPlayer) continue;
                        if (p.Value.MaxHealth > 0 && p.Value.Health <= 0) continue;
                        uint pid = p.Value.PartyID;
                        if (!byParty.TryGetValue(pid, out var list))
                        {
                            list = new List<KeyValuePair<int, Player>>(4);
                            byParty[pid] = list;
                        }
                        list.Add(p);
                    }
                    foreach (var g in byParty.Values)
                    {
                        if (g.Count <= 1) continue;
                        for (int i = 0; i < g.Count; i++)
                        {
                            for (int j = i + 1; j < g.Count; j++)
                            {
                                if (g[i].Value.PartyID == 0) continue;
                                var startPos = GetMapPos(g[i].Value.Location, 1);
                                var endPos = GetMapPos(g[j].Value.Location, 1);
                                var lineColor = ImGui.GetColorU32(generateTeamColor(g[i].Value.PartyID));
                                backgroundDrawList.AddLine(startPos, endPos, lineColor, 2.0f);
                            }
                        }
                    }
                }
            }
            else
            {
                // Centered Text
                backgroundDrawList.AddText(new Vector2((io.DisplaySize.X / 2) - 100, (io.DisplaySize.Y / 2) - 10), ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1f)), "Waiting for game...");
            }

            var fps = (float)Math.Round(io.Framerate, 1);
            backgroundDrawList.AddText(new Vector2(2f, 2f), ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1f)), $"{fps}");
            
            // EXTRA MONITOR SHIT
            //backgroundDrawList.AddText(new Vector2(2f, 16f), ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1f)), $"Selected Monitor Index:{selectedMonitorIndex}");
            //var monitor = GLFW.GetMonitors()[selectedMonitorIndex];
            //GLFW.GetMonitorWorkarea(monitor, out int x, out int y, out int width, out int height);
            //backgroundDrawList.AddText(new Vector2(2f, 32f), ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1f)), $"{width}x{height}");
            
            RenderMenu(io);
            
        }

        private void DrawPlayerPill(ImDrawListPtr drawListBg, Player player, float rotation, Vector2 screenPos, uint clr, float relativeHeight)
        {
            float num = this.imageSizeCurrent.X / textureSize.X * Config.ActiveConfig.UIScale; // this._config.WidgetScale
            double num2 = rotation.ToRadians();
            drawListBg.AddCircleFilled(screenPos, 5f * num, ImGui.GetColorU32(ImGuiCol.Text));
            drawListBg.AddCircleFilled(screenPos, 4f * num, clr);
            float num3 = 4.98999f;
            float num4 = 3.98999f;
            double num5 = (rotation - 90f).ToRadians();
            Vector2 vector2 = new Vector2((float)((double)screenPos.X + Math.Cos(num5) * (double)(num3 * num)), (float)((double)screenPos.Y + Math.Sin(num5) * (double)(num3 * num)));
            Vector2 vector3 = new Vector2((float)((double)screenPos.X + Math.Cos(num5) * (double)(num4 * num)), (float)((double)screenPos.Y + Math.Sin(num5) * (double)(num4 * num)));
            double num6 = (rotation + 90f).ToRadians();
            Vector2 vector4 = new Vector2((float)((double)screenPos.X + Math.Cos(num6) * (double)(num3 * num)), (float)((double)screenPos.Y + Math.Sin(num6) * (double)(num3 * num)));
            Vector2 vector5 = new Vector2((float)((double)screenPos.X + Math.Cos(num6) * (double)(num4 * num)), (float)((double)screenPos.Y + Math.Sin(num6) * (double)(num4 * num)));
            Vector2 vector6 = new Vector2((float)((double)screenPos.X + Math.Cos(num2) * (double)(12f * num)), (float)((double)screenPos.Y + Math.Sin(num2) * (double)(12f * num)));
            Vector2 vector7 = new Vector2((float)((double)screenPos.X + Math.Cos(num2) * (double)(10f * num)), (float)((double)screenPos.Y + Math.Sin(num2) * (double)(10f * num)));
            Vector2 vector8 = new Vector2((float)((double)screenPos.X + Math.Cos(num2) * (8.77 * (double)num)), (float)((double)screenPos.Y + Math.Sin(num2) * (8.77 * (double)num)));
            drawListBg.AddTriangleFilled(vector2, vector4, vector6, ImGui.GetColorU32(ImGuiCol.Text));
            drawListBg.AddTriangleFilled(vector3, vector5, vector7, clr);

            if (Config.ActiveConfig.ShowAimlines)
            {
                Vector2 vector = new Vector2((float)((double)screenPos.X + Math.Cos(num2) * (double)(Config.ActiveConfig.AimlineLength * num)), (float)((double)screenPos.Y + Math.Sin(num2) * (double)(Config.ActiveConfig.AimlineLength * num)));
                drawListBg.AddLine(vector8, vector, ImGui.GetColorU32(ImGuiCol.Text), 2f * num);
                drawListBg.AddCircleFilled(vector, 1f * num, ImGui.GetColorU32(ImGuiCol.Text));
                drawListBg.AddLine(vector8, vector, clr, 1f * num);
                drawListBg.AddCircleFilled(vector, 0.5f * num, clr);
            }
            if (relativeHeight != 0 && Config.ActiveConfig.ShowHeightIndicators)
            {
                // Draw triangle inside player pill to indicate height difference
                int relativeHeightInt = relativeHeight > 0 ? -1 : 1;
                Vector2 point1 =
                    relativeHeightInt > 0
                        ? new Vector2(screenPos.X - 20, screenPos.Y - 10)
                        : new Vector2(screenPos.X - 20, screenPos.Y + 10);
                Vector2 point2 = new Vector2(screenPos.X - 15, screenPos.Y);
                Vector2 point3 = new Vector2(screenPos.X - 25, screenPos.Y);
                drawListBg.AddTriangleFilled(point1, point2, point3, ImGui.GetColorU32(ImGuiCol.Text));
            }
            float effectiveRadius = 5f * (this.imageSizeCurrent.X / textureSize.X * Config.ActiveConfig.UIScale);
            playerPills.Add((screenPos, effectiveRadius, player));
        }
        
        //private void DrawPlayerSkeleton(Dictionary<int, Bone> bones, Vector3 location, string textToDraw, int health)
        //{
        //    foreach (var (boneFrom, boneTo) in Bones.BoneConnections)
        //    {
        //        var bone1 = bones[(int)boneFrom];
        //        var bone2 = bones[(int)boneTo];

        //        //bool bonePos1 = WorldToScreen(bone1.Origin, out Vector2 bone1Screen, Memory.ViewMatrix);
        //        //bool bonePos2 = WorldToScreen(bone2.Origin, out Vector2 bone2Screen, Memory.ViewMatrix);

        //        //if (!bonePos1 || !bonePos2) break;
        //        //ImGui.GetBackgroundDrawList().AddLine(bone1Screen, bone2Screen, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1f)), 1.25f);
        //    }
        //}
        private float Interpolate(float t1, float t2, float progress)
        {
            if (t1 == t2)
            {
                return t1;
            }
            float num = t2 - t1;
            return t1 + num * progress;
        }

        private Vector2 GetMapPos(Vector3 pos, int preview = -1)
        {
            MapPosition mapPosition = pos.ToMapPos(_selectedMap);

            return this.positionCurrent + new Vector2(mapPosition.X * (this.imageSizeCurrent.X / textureSize.X), mapPosition.Y * (this.imageSizeCurrent.Y / textureSize.Y));
        }

        public static Dictionary<string, Bitmap> bitmapCache = new Dictionary<string, Bitmap>();
        public static IntPtr LoadTexture(string fileName, FileStream stream, out Vector2 size)
        {
            if (!bitmapCache.ContainsKey(fileName))
            {
                Bitmap newBitmap = new Bitmap(stream);
                bitmapCache.Add(fileName, newBitmap);
            }
            Bitmap bitmap;
            bitmapCache.TryGetValue(fileName, out bitmap);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            int num;
            GL.GenTextures(1, out num);
            GL.BindTexture(TextureTarget.Texture2D, num);
            BitmapData bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), (ImageLockMode)1, (System.Drawing.Imaging.PixelFormat)2498570);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bitmapData.Width, bitmapData.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmapData.Scan0);
            bitmap.UnlockBits(bitmapData);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 9728);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 9728);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, 10497);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, 10497);
            size.X = (float)bitmapData.Width;
            size.Y = (float)bitmapData.Height;
            return new IntPtr(num);
        }


        public ref bool ReferenceToBool(bool value)
        {
            return ref value;
        }

        public ref float ReferenceToFloat(float value)
        {
            return ref value;
        }

        public void RenderMenu(ImGuiIOPtr io)
        {
            bool flag = true;
            float num = 2f;
            float num2 = 2f;
            Vector2 vector = new Vector2(io.DisplaySize.X - 220f - num, num2);
            ImGui.SetNextWindowPos(vector, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(200f, 2f), ImGuiCond.Once);
            if (ImGui.Begin("Navigation", ref flag, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove))
            {
                ImGui.Text("Options");
                ImGui.Spacing();
                ImGui.Separator();

                DrawMenuButton("Free Mode", ref _isFreeMode);
                DrawMenuCheckbox("Show POIs", ref ReferenceToBool(Config.ActiveConfig.ShowPOIs), "ShowPOIs", true);
                //DrawMenuButton("Toggle POIs", ref ReferenceToBool(Config.ActiveConfig.ShowPOIs), "ShowPOIs", true);
                DrawMenuButton("Settings", ref _showRadarSettings);

                //ImGui.Spacing();
                //ImGui.Separator();
                //ImGui.Text("DMAWarehouse");
                ImGui.End();
            }

            if (_showRadarSettings)
            {
                ImGui.SetNextWindowSize(new Vector2(200f, 0f));
                if(ImGui.Begin("Radar Settings", ref _showRadarSettings, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse))
                {

                    ImGui.Text("Blip Settings");

                    // Shrines/Portals/Chests/Levers
                    DrawMenuCheckbox("Show Shrines", ref ReferenceToBool(Config.ActiveConfig.ShowShrines), "ShowShrines", true);
                    DrawMenuCheckbox("Show Portals", ref ReferenceToBool(Config.ActiveConfig.ShowPortals), "ShowPortals", true);
                    DrawMenuCheckbox("Show Chests", ref ReferenceToBool(Config.ActiveConfig.ShowChests), "ShowChests", true);
                    DrawMenuCheckbox("Show Levers", ref ReferenceToBool(Config.ActiveConfig.ShowLevers), "ShowLevers", true);
                    DrawMenuCheckbox("Show Ore", ref ReferenceToBool(Config.ActiveConfig.ShowOre), "ShowOre", true);
                    
                    // Enemies
                    DrawMenuCheckbox("Show Mimics", ref ReferenceToBool(Config.ActiveConfig.ShowMimics), "ShowMimics", true);
                    DrawMenuCheckbox("Show Bosses", ref ReferenceToBool(Config.ActiveConfig.ShowBosses), "ShowBosses", true);
                    DrawMenuCheckbox("Show Mobs", ref ReferenceToBool(Config.ActiveConfig.ShowMobs), "ShowMobs", true);
                    // Keys + Artifacts
                    DrawMenuCheckbox("Show Special Items", ref ReferenceToBool(Config.ActiveConfig.ShowSpecialItems), "ShowSpecialItems", true);
                    
                    ImGui.Separator();

                    DrawMenuCheckbox("Show Height Indicators", ref ReferenceToBool(Config.ActiveConfig.ShowHeightIndicators), "ShowHeightIndicators", true);
                    DrawMenuCheckbox("Draw Team Lines", ref ReferenceToBool(Config.ActiveConfig.ShowTeamLines), "ShowTeamLines", true);
                    DrawMenuCheckbox("Show Aim Line", ref ReferenceToBool(Config.ActiveConfig.ShowAimlines), "ShowAimlines", true);
                    
                    ImGui.Spacing();

                    ImGui.Text("Aim Line Length");
                    ImGui.SetNextItemWidth(200f);
                    DrawMenuSlider("##aimLineLength", ref ReferenceToFloat(Config.ActiveConfig.AimlineLength), 25f, 100f, "AimlineLength", true);

                    ImGui.Separator();

                    ImGui.Text("UI Scale");
                    ImGui.SetNextItemWidth(200f);
                    DrawMenuSlider("##renderScale", ref ReferenceToFloat(Config.ActiveConfig.UIScale), 0.5f, 2.5f, "UIScale", true);

                    ImGui.End();
                }
            }

            if (0 == 1) // Show Menu
            {
                Vector2 cursorPos = new Vector2(500f, 0f);
                ImGui.SetNextWindowSize(cursorPos, ImGuiCond.Always);
                bool flag2 = true;
                if (ImGui.Begin("Menu", ref flag2, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse))
                {
                    if (0 == 0) //this.viewSettingsTab == 0
                    {
                        ImGui.Separator();
                        this.RenderTabs(new string[] { "Render", "Maps", "Styling", "Aimview" }, ref this.settingsTab);
                        ImGui.Spacing();
                        if (true) //this.settingsTab == 0
                        {
                            ImGui.Columns(2);
                            ImGui.PushItemWidth(-1f);
                            //bool playersEnabled = this._config.PlayersEnabled;
                            bool playersEnabled = true;
                            ImGui.Checkbox("Render Players", ref playersEnabled);
                            //this._config.PlayersEnabled = playersEnabled;
                            //bool lootEnabled = this._config.LootEnabled;
                            bool lootEnabled = true;
                            ImGui.Checkbox("Render C4", ref lootEnabled);
                            //this._config.LootEnabled = lootEnabled;
                            //ImGui.SameLine();
                            //Vector4 vector8 = ImGui.ColorConvertU32ToFloat4(this._config.LootColorNormal);
                            //ImGui.ColorEdit4("##lootColorNormal", ref vector8, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoInputs);
                            //this._config.LootColorNormal = ImGui.ColorConvertFloat4ToU32(vector8);
                            //ImGui.SameLine();
                            //Vector4 vector9 = ImGui.ColorConvertU32ToFloat4(this._config.LootColorImportant);
                            //ImGui.ColorEdit4("##lootColorImportant", ref vector9, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoInputs);
                            //this._config.LootColorImportant = ImGui.ColorConvertFloat4ToU32(vector9);
                            int zoomMode = 1;
                            ImGui.Text("Zoom Mode");
                            ImGui.Combo("##Zoom Mode", ref zoomMode, "Cursor\0Screen Center\0\0");
                            ImGui.PopItemWidth();
                            ImGui.NextColumn();
                            ImGui.PushItemWidth(-1f);
                            int aimlinePmcLength = 5;
                            ImGui.Text("Aimline Enemy");
                            ImGui.SliderInt("##LenghtEnemy", ref aimlinePmcLength, 25, 1000);
                            //this._config.AimlinePmcLength = aimlinePmcLength;
                            //int aimlineTeamLength = this._config.AimlineTeamLength;
                            int aimlineTeamLength = 5;
                            ImGui.Text("Aimline Team");
                            ImGui.SliderInt("##LenghtTeam", ref aimlineTeamLength, 25, 1000);
                            //this._config.AimlineTeamLength = aimlineTeamLength;
                            int aimlineLocalLength = 5;
                            ImGui.Text("Aimline Local");
                            ImGui.SliderInt("##Lenght Local", ref aimlineLocalLength, 25, 1000);
                            //this._config.AimlineLocalLength = aimlineLocalLength;
                            bool aimlineWarning = true;
                            ImGui.Checkbox("Aimline Warning", ref aimlineWarning);
                            //this._config.AimlineWarning = aimlineWarning;
                            if (aimlineWarning)
                            {
                                float aimlineWarningMultiplier = 10.0f;
                                ImGui.SliderFloat("##Aimline Warning Multiplier", ref aimlineWarningMultiplier, 1f, 10f);
                                //this._config.AimlineWarningMultiplier = aimlineWarningMultiplier;
                            }
                            bool checkboxAmmoScav = true;
                            //if (ImGui.Checkbox("Show Scav Ammo Type", ref checkboxAmmoScav))
                            //{
                            //    //this._config.CheckAmmoScav = this.checkboxAmmoScav;
                            //}
                            bool checkboxStreamerMode = true;
                            if (ImGui.Checkbox("Streamer mode", ref checkboxStreamerMode))
                            {
                                //this._config.StreamerMode = this.checkboxStreamerMode;
                            }
                            ImGui.PopItemWidth();
                            ImGui.Columns(1);
                            ImGui.Spacing();
                            ImGui.Spacing();
                        }
                    }
                    cursorPos = ImGui.GetCursorPos();
                    ImGui.SetWindowPos(new Vector2(io.DisplaySize.X / 2f - ImGui.GetWindowSize().X / 2f, 150f), ImGuiCond.Always);
                    if (!flag2)
                    {
                        //this.showMenu = 0;
                    }
                    ImGui.End();
                }
            }
        }

        public void DrawMenuButton(string buttonLabel, ref bool controlArg, string ConfigText = "none", bool isConfigSetting = false)
        {
            ImGui.Spacing();
            ImGui.ColorButton($"##{buttonLabel}", controlArg ? new Vector4(0f, 1f, 0f, 1f) : new Vector4(0.5f, 0.5f, 0.5f, 1f), ImGuiColorEditFlags.NoTooltip, new Vector2(5f, ImGui.CalcTextSize(buttonLabel).Y + ImGui.GetStyle().FramePadding.Y * 2f)); ;
            ImGui.SameLine(10f);

            if (ImGui.Button(buttonLabel, new Vector2(200, 0f)))
            {
                controlArg = !controlArg;
                if(isConfigSetting)
                {
                    Console.WriteLine($"Setting {ConfigText} to {controlArg}");
                    Config.SetBoolean(ConfigText, controlArg);
                }
            }
        }

        public void DrawMenuCheckbox(string checkboxLabel, ref bool controlArg, string ConfigText = "none", bool isConfigSetting = false)
        {
            if(ImGui.Checkbox(checkboxLabel, ref controlArg))
            {
                if(isConfigSetting)
                {
                    Config.SetBoolean(ConfigText, controlArg);
                }
            }
        }

        public void DrawMenuSlider(string sliderLabel, ref float controlArg, float min, float max, string ConfigText = "none", bool isConfigSetting = false)
        {
            if(ImGui.SliderFloat(sliderLabel, ref controlArg, min, max))
            {
                controlArg = Math.Clamp(controlArg, min, max);
                // Persist once after the drag/edit ends; avoids per-frame disk/log churn that can hitch rendering.
                if(isConfigSetting && ImGui.IsItemDeactivatedAfterEdit())
                {
                    Config.SetFloat(ConfigText, controlArg);
                }
            }
        }

        public unsafe void RenderTabs(string[] tabs, ref int currentTab)
        {
            Vector2 vector = ImGui.GetWindowSize() - ImGui.GetStyle().WindowPadding - new Vector2(ImGui.GetStyle().ItemSpacing.X * (float)tabs.Length, 0f);
            foreach (var tab in tabs.Select((string value, int i) => new { i, value }))
            {
                string value2 = tab.value;
                int j = tab.i;
                uint num = ImGui.GetColorU32(ImGuiCol.Button);
                if (currentTab == j)
                {
                    num = ImGui.GetColorU32(ImGuiCol.TitleBg);
                }
                ImGui.PushStyleColor(ImGuiCol.Button, num);
                if (ImGui.Button(value2, new Vector2(vector.X / (float)tabs.Length, 0f)))
                {
                    currentTab = j;
                }
                ImGui.PopStyleColor();
                if (j != tabs.Length - 1)
                {
                    ImGui.SameLine();
                }
            }
        }

        public static Vector4 ColorToVector4(float r, float g, float b, float a)
        {
            return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        public unsafe bool InitStyle()
        {
            ImGuiStylePtr style = ImGui.GetStyle();
            //RangeAccessor<Vector4> colors = style.Colors;
            //style.WindowBorderSize = 0f;
            //style.WindowTitleAlign = new Vector2(0.5f, 0.5f);
            //style.WindowMinSize = new Vector2(100f, 20f);
            //style.FramePadding = new Vector2(6f, 3f);
            //style.WindowPadding = new Vector2(6f, 3f);
            //style.WindowRounding = 3f;
            //style.FrameRounding = 1f;
            //Vector4 vector = ColorToVector4(30f, 30f, 30f, 150f);
            //colors[10] = vector;
            //colors[11] = vector;
            //colors[12] = vector;
            //colors[30] = ColorToVector4(0f, 0f, 0f, 0f);
            //colors[32] = ColorToVector4(0f, 0f, 0f, 0f);
            //colors[31] = ColorToVector4(0f, 0f, 0f, 0f);
            //colors[21] = ColorToVector4(31f, 30f, 31f, 255f);
            //colors[23] = ColorToVector4(31f, 30f, 31f, 255f);
            //colors[22] = ColorToVector4(128f, 128f, 128f, 100f);
            //colors[27] = ColorToVector4(70f, 70f, 70f, 255f);
            //colors[29] = ColorToVector4(76f, 76f, 76f, 255f);
            //colors[28] = ColorToVector4(76f, 76f, 76f, 255f);
            //colors[7] = ColorToVector4(37f, 36f, 37f, 255f);
            //colors[9] = ColorToVector4(150f, 54f, 24f, 255f);
            //colors[24] = ColorToVector4(150f, 54f, 24f, 255f);
            //colors[8] = ColorToVector4(130f, 50f, 20f, 255f);
            //colors[26] = ColorToVector4(130f, 54f, 24f, 255f);
            //colors[25] = ColorToVector4(130f, 50f, 20f, 255f;
            //colors[18] = new Vector4(0.9f, 0.9f, 0.9f, 0.5f);
            //colors[19] = new Vector4(0.8f, 0.78f, 0.78f, 0.8f);
            //colors[20] = ColorToVector4(128f, 128f, 128f, 255f);
            //colors[0] = new Vector4(0.8f, 0.8f, 0.83f, 1f);
            //colors[1] = new Vector4(0.24f, 0.23f, 0.29f, 1f);
            //colors[49] = new Vector4(0.25f, 1f, 0f, 0.43f);
            //colors[2] = new Vector4(0.06f, 0.05f, 0.07f, 1f);
            //colors[3] = new Vector4(0.07f, 0.07f, 0.09f, 1f);
            //colors[4] = new Vector4(0.07f, 0.07f, 0.09f, 1f);
            //colors[5] = new Vector4(0.92f, 0.92f, 0.92f, 0.4f);
            //colors[6] = new Vector4(0.95f, 0.95f, 0.95f, 0.4f);
            //colors[7] = new Vector4(0.1f, 0.09f, 0.12f, 1f);
            //colors[8] = new Vector4(0.24f, 0.23f, 0.29f, 1f);
            //colors[9] = new Vector4(0.56f, 0.56f, 0.58f, 1f);
            //colors[10] = new Vector4(0.1f, 0.09f, 0.12f, 1f);
            //colors[12] = new Vector4(0.07f, 0.07f, 0.09f, 1f);
            //colors[11] = new Vector4(0.07f, 0.07f, 0.09f, 1f);
            //colors[13] = new Vector4(0.1f, 0.09f, 0.12f, 1f);
            //colors[14] = new Vector4(0.1f, 0.09f, 0.12f, 1f);
            //colors[15] = new Vector4(0.8f, 0.8f, 0.83f, 0.31f);
            //colors[17] = new Vector4(0.56f, 0.56f, 0.58f, 1f);
            //colors[16] = new Vector4(0.06f, 0.05f, 0.07f, 1f);
            //colors[18] = new Vector4(0.8f, 0.8f, 0.83f, 0.31f);
            //colors[19] = new Vector4(0.8f, 0.8f, 0.83f, 0.31f);
            //colors[20] = new Vector4(0.06f, 0.05f, 0.07f, 1f);
            //colors[21] = new Vector4(0.1f, 0.09f, 0.12f, 1f);
            //colors[22] = new Vector4(0.24f, 0.23f, 0.29f, 1f);
            //colors[23] = new Vector4(0.56f, 0.56f, 0.58f, 1f);
            //colors[24] = new Vector4(0.1f, 0.09f, 0.12f, 1f);
            //colors[25] = new Vector4(0.56f, 0.56f, 0.58f, 1f);
            //colors[26] = new Vector4(0.06f, 0.05f, 0.07f, 1f);
            //colors[30] = new Vector4(0f, 0f, 0f, 0f);
            //colors[31] = new Vector4(0.56f, 0.56f, 0.58f, 1f);
            //colors[32] = new Vector4(0.06f, 0.05f, 0.07f, 1f);
            //colors[40] = new Vector4(0.4f, 0.39f, 0.38f, 0.63f);
            //colors[41] = new Vector4(0.25f, 1f, 0f, 1f);
            //colors[42] = new Vector4(0.4f, 0.39f, 0.38f, 0.63f);
            //colors[43] = new Vector4(0.25f, 1f, 0f, 1f);

            style.Colors[(int)ImGuiCol.Text] = new Vector4(1.00f, 1.00f, 1.00f, 1.00f);
            style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.50f, 0.50f, 1.00f);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.10f, 0.10f, 0.10f, 1.00f);
            style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
            style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.19f, 0.19f, 0.19f, 0.92f);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(0.19f, 0.19f, 0.19f, 0.29f);
            style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.24f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.05f, 0.05f, 0.05f, 0.54f);
            style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.19f, 0.19f, 0.19f, 0.54f);
            style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.20f, 0.22f, 0.23f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.06f, 0.06f, 0.06f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.14f, 0.14f, 0.14f, 1.00f);
            style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.05f, 0.05f, 0.05f, 0.54f);
            style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.34f, 0.34f, 0.34f, 0.54f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.40f, 0.40f, 0.40f, 0.54f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.56f, 0.56f, 0.56f, 0.54f);
            style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.33f, 0.67f, 0.86f, 1.00f);
            style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.34f, 0.34f, 0.34f, 0.54f);
            style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.56f, 0.56f, 0.56f, 0.54f);
            style.Colors[(int)ImGuiCol.Button] = new Vector4(0.05f, 0.05f, 0.05f, 0.54f);
            style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.19f, 0.19f, 0.19f, 0.54f);
            style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.20f, 0.22f, 0.23f, 1.00f);
            style.Colors[(int)ImGuiCol.Header] = new Vector4(0.00f, 0.00f, 0.00f, 0.52f);
            style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.00f, 0.00f, 0.00f, 0.36f);
            style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.20f, 0.22f, 0.23f, 0.33f);
            style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.28f, 0.28f, 0.28f, 0.29f);
            style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.44f, 0.44f, 0.44f, 0.29f);
            style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.40f, 0.44f, 0.47f, 1.00f);
            style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.28f, 0.28f, 0.28f, 0.29f);
            style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.44f, 0.44f, 0.44f, 0.29f);
            style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.40f, 0.44f, 0.47f, 1.00f);
            style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.00f, 0.00f, 0.00f, 0.52f);
            style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.14f, 0.14f, 0.14f, 1.00f);
            style.Colors[(int)ImGuiCol.TabActive] = new Vector4(0.20f, 0.20f, 0.20f, 0.36f);
            style.Colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.00f, 0.00f, 0.00f, 0.52f);
            style.Colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.14f, 0.14f, 0.14f, 1.00f);
            style.Colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.33f, 0.67f, 0.86f, 1.00f);
            style.Colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(1.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(1.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(1.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(1.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.52f);
            style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.00f, 0.00f, 0.00f, 0.52f);
            style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.28f, 0.28f, 0.28f, 0.29f);
            style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
            style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.00f, 1.00f, 1.00f, 0.06f);
            style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.20f, 0.22f, 0.23f, 1.00f);
            style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.33f, 0.67f, 0.86f, 1.00f);
            style.Colors[(int)ImGuiCol.NavHighlight] = new Vector4(1.00f, 0.00f, 0.00f, 1.00f);
            style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.00f, 0.00f, 0.00f, 0.70f);
            style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(1.00f, 0.00f, 0.00f, 0.20f);
            style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(1.00f, 0.00f, 0.00f, 0.35f);

            style.WindowPadding = new Vector2(8.00f, 8.00f);
            style.FramePadding = new Vector2(5.00f, 2.00f);
            style.CellPadding = new Vector2(6.00f, 6.00f);
            style.ItemSpacing = new Vector2(6.00f, 6.00f);
            style.ItemInnerSpacing = new Vector2(6.00f, 6.00f);
            style.TouchExtraPadding = new Vector2(0.00f, 0.00f);
            style.IndentSpacing = 25;
            style.ScrollbarSize = 15;
            style.GrabMinSize = 10;
            style.WindowBorderSize = 1;
            style.ChildBorderSize = 1;
            style.PopupBorderSize = 1;
            style.FrameBorderSize = 1;
            style.TabBorderSize = 1;
            style.WindowRounding = 7;
            style.ChildRounding = 4;
            style.FrameRounding = 3;
            style.PopupRounding = 4;
            style.ScrollbarRounding = 9;
            style.GrabRounding = 3;
            style.LogSliderDeadzone = 4;
            style.TabRounding = 4;

            return true;
        }

        //public IntPtr LoadTextureFromMemory(byte[] imageData, string filename, out Vector2 size)
        //{
        //    string text = filename.Replace(".komar", "");
            
        //    Bitmap bitmap2;
        //    //_loadedBitmaps.TryGetValue(text, out bitmap2);
        //    GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
        //    int num;
        //    GL.GenTextures(1, out num);
        //    GL.BindTexture(TextureTarget.Texture2D, num);
        //    BitmapData bitmapData = bitmap2.LockBits(new System.Drawing.Rectangle(0, 0, bitmap2.Width, bitmap2.Height), 1, 2498570);
        //    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bitmapData.Width, bitmapData.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmapData.Scan0);
        //    bitmap2.UnlockBits(bitmapData);
        //    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 9728);
        //    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 9728);
        //    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, 10497);
        //    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, 10497);
        //    size.X = (float)bitmapData.Width;
        //    size.Y = (float)bitmapData.Height;
        //    return new IntPtr(num);
        //}

        public static void ApplyMap()
        {

        }

        // private void HandleMapChange()
        // {
        //     lock (_renderLock)
        //     {
        //         try
        //         {
        //             //_selectedMap = _maps[_mapSelectionIndex]; // Swap map
        //             //if (_loadedBitmaps is not null)
        //             //{
        //             //    _loadedBitmaps // Cleanup resources
        //             //}
        //             _loadedBitmaps = new Bitmap[_selectedMap.ConfigFile.mapLayers.Count];
        //             for (int i = 0; i < _loadedBitmaps.Length; i++)
        //             {
        //                 using (var stream = File.Open(_selectedMap.ConfigFile.mapLayers[i].filename, FileMode.Open, FileAccess.Read))
        //                 {
        //                     Bitmap bitmap = new(stream);
        //                     _loadedBitmaps[i] = bitmap;
        //                 }
        //             }
        //         }
        //         catch (Exception ex)
        //         {
        //             throw new Exception($"ERROR loading {_selectedMap.ConfigFile.mapLayers[0].filename}: {ex}");
        //         }
        //     }
        // }

        private static void PointCenterMap(Vector2 pos)
        {
            position = new Vector2(pos.X * (imageSize.X / textureSize.X) * -1f, pos.Y * (imageSize.Y / textureSize.Y) * -1f) - renderWindowPos / 2f + renderWindowSize / 2f;
        }

        private void PointCenterMap(Vector3 pos)
        {
            MapPosition mapPosition = pos.ToMapPos(_selectedMap);
            position = new Vector2(mapPosition.X * (imageSize.X / textureSize.X) * -1f, mapPosition.Y * (imageSize.Y / textureSize.Y) * -1f) - renderWindowPos / 2f + renderWindowSize / 2f;
        }

        public static bool LoadMaps()
        {
            while(Memory.Ready == false)
            {
                Thread.Sleep(100);
            }
            var dir = new DirectoryInfo($"{Environment.CurrentDirectory}\\Maps");
            if (!dir.Exists) dir.Create();
            var configs = dir.GetFiles("*.json"); // Get all JSON files
            if (configs.Length == 0) throw new IOException("No .json map configs found!");

            foreach (var config in configs)
            {
                var name = Path.GetFileNameWithoutExtension(config.Name); // map name ex. 'CUSTOMS' w/o extension
                Program.Log($"Loading Map: {name}");
                var mapConfig = MapConfig.LoadFromFile(config.FullName);
                var map = new Map(
                    name,
                    mapConfig,
                    config.FullName,
                    mapConfig.GameName
                );

                map.ConfigFile.mapLayers = map.ConfigFile.mapLayers.OrderBy(x => x.minHeight).ToList(); // 'Lowest' Height starting at Index 0
                _maps.Add(map);
            }
            if (Memory.game.InGame == false) return true;
            // Wait for a real map name — otherwise we would pick the wrong JSON (first folder entry).
            for (int w = 0; w < 100; w++)
            {
                if (IsValidGameMapName(Memory.game.CurrentMapName))
                    break;
                Thread.Sleep(50);
            }
            var gameMap = Memory.game.CurrentMapName;
            var strip = StripDungeonMapSuffix(gameMap);
            _selectedMap = _maps.FirstOrDefault(m => m.ConfigFile.GameName.Any(gn =>
                string.Equals(gn, gameMap, StringComparison.OrdinalIgnoreCase)
                || string.Equals(StripDungeonMapSuffix(gn), strip, StringComparison.OrdinalIgnoreCase)));
            if (_selectedMap == null)
            {
                Program.Log($"No map JSON matches '{gameMap}'. Using first entry; add gameName to the correct Maps\\*.json.");
                _selectedMap = _maps.First();
            }
            Program.Log($"Selected Map: {_selectedMap.Name}");
            try
            {
                //Program.Log($"Loading Initial Map: {_selectedMap.Name}");
                //if (_loadedBitmaps is not null)
                //{
                //    foreach (var bitmap in _loadedBitmaps) bitmap?.Dispose(); // Cleanup resources
                //}
                _loadedBitmaps = new nint[_selectedMap.ConfigFile.mapLayers.Count];
                for (int i = 0; i < _loadedBitmaps.Length; i++)
                {
                    using (var stream = File.Open(_selectedMap.ConfigFile.mapLayers[i].filename, FileMode.Open, FileAccess.Read))
                    {
                        try
                        {
                            var bitmap = LoadTexture(_selectedMap.ConfigFile.mapLayers[i].filename, stream, out Vector2 size);
                            textureSize = size;
                            _loadedBitmaps[i] = bitmap;
                            Program.Log($"Loaded Map Layer: {stream.Name}");
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"ERROR loading map: {i} {ex}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"ERROR loading initial map: {ex}");
            }

            return true;
        }

        /// <summary>Strip known Dark and Darker level suffixes so e.g. Ruins_7x7_R_P matches Ruins_7x7_* entries.</summary>
        private static string StripDungeonMapSuffix(string? mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                return "";
            return mapName
                .Replace("_HR_P", "", StringComparison.OrdinalIgnoreCase)
                .Replace("_N_P", "", StringComparison.OrdinalIgnoreCase)
                .Replace("_R_P", "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Map name from GWorld is not ready yet (race before first MapReadLoop).</summary>
        private static bool IsValidGameMapName(string? mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName)) return false;
            if (mapName.Equals("<empty>", StringComparison.OrdinalIgnoreCase)) return false;
            if (mapName.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void HandleMapChange()
        {
            try
            {
                if (Memory.game.InGame == false)
                {
                    _lastHandledGameMapStrip = "";
                    return;
                }

                var gameMap = Memory.game.CurrentMapName;
                if (!IsValidGameMapName(gameMap))
                    return;

                var strip = StripDungeonMapSuffix(gameMap);

                // Compare normalized map id so Ruins_7x7_R_P and Ruins_7x7_N_P do not reload every frame.
                if (string.Equals(strip, _lastHandledGameMapStrip, StringComparison.OrdinalIgnoreCase))
                    return;

                var newMap = _maps.FirstOrDefault(m => m.ConfigFile.GameName.Any(gn =>
                    string.Equals(gn, gameMap, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(StripDungeonMapSuffix(gn), strip, StringComparison.OrdinalIgnoreCase)));

                // Do not fall back to _maps.First() — that was loading Crypt_01 before Ruins was known and broke alignment.
                if (newMap == null)
                    return;

                if (ReferenceEquals(_selectedMap, newMap))
                {
                    _lastHandledGameMapStrip = strip;
                    return;
                }

                _lastHandledGameMapStrip = strip;
                _selectedMap = newMap;
                LoadMapTextures(newMap);
                Program.Log($"Map changed to: {newMap.Name}");
            }
            catch (Exception ex)
            {
                //Program.Log($"ERROR changing map: {ex}");
            }
        }


        private void LoadMapTextures(Map map)
        {
            ActivityLog.Info("Map", $"LoadMapTextures: {map.Name} layers={map.ConfigFile.mapLayers.Count}");
            DisposeOldTextures(); // Dispose old textures first
            _loadedBitmaps = new nint[map.ConfigFile.mapLayers.Count];
            for (int i = 0; i < _loadedBitmaps.Length; i++)
            {
                using (var stream = File.Open(map.ConfigFile.mapLayers[i].filename, FileMode.Open, FileAccess.Read))
                {
                    try
                    {
                        var bitmap = LoadTexture(map.ConfigFile.mapLayers[i].filename, stream, out Vector2 size);
                        textureSize = size;
                        _loadedBitmaps[i] = bitmap;
                        //Program.Log($"Loaded Map Layer: {stream.Name}");
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"ERROR loading map layer: {ex}");
                    }
                }
            }
        }

        private void DisposeOldTextures()
        {
            if (_loadedBitmaps != null)
            {
                foreach (var texture in _loadedBitmaps)
                {
                    if (texture != IntPtr.Zero)
                    {
                        GL.DeleteTexture(texture.ToInt32());
                    }
                }
            }
        }
       
        Vector4 generateTeamColor(uint partyID)
        {
            if (partyID == 0)
            {
                return new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            }
            var randRed = (float)(partyID % 13);
            var randGreen = (float)(partyID % 9);
            var randBlue = (float)(partyID % 15);
            var red = 1.0f - (randRed / 18.0f);
            var green = 1.0f - (randGreen / 12.0f);
            var blue = 1.0f - (randBlue / 21.0f);
            return new Vector4(red, green, blue, 1.0f);
        }
        void CheckForHoverAndShowTooltip()
        {
            Vector2 mousePos = ImGui.GetMousePos();
            foreach (var (Position, Radius, PlayerData) in playerPills)
            {
                if (Vector2.Distance(mousePos, Position) <= Radius)
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Name: {PlayerData.Name}");
                    ImGui.Text($"Weapon(s): {PlayerData.Weapon}");
                    ImGui.Text($"Class: {PlayerData.Class}");
                    ImGui.Text($"Health: {MathF.Round(PlayerData.Health)}/{MathF.Round(PlayerData.MaxHealth)}");
                    ImGui.Text($"Level: {PlayerData.Level}");
                    ImGui.Text($"Strength: {PlayerData.Strength}");
                    ImGui.Text($"Vigor: {PlayerData.Vigor}");
                    ImGui.Text($"Agility: {PlayerData.Agility}");
                    ImGui.Text($"Dexterity: {PlayerData.Dexterity}");
                    ImGui.Text($"Will: {PlayerData.Will}");
                    ImGui.Text($"Knowledge: {PlayerData.Knowledge}");
                    ImGui.Text($"Resourcefulness: {PlayerData.Resourcefulness}");
                    ImGui.Text($"PhysicalDamageWeaponPrimary: {PlayerData.PhysicalDamageWeaponPrimary}");
                    ImGui.Text($"PhysicalDamageBase: {PlayerData.PhysicalDamageBase}");
                    ImGui.Text($"PhysicalPower: {PlayerData.PhysicalPower}");
                    ImGui.Text($"ArmorRating: {PlayerData.ArmorRating}");

                    // Add more details as needed...
                    ImGui.EndTooltip();
                    break; // Assuming only one pill can be hovered over at a time
                }
            }
        }
    }
}
