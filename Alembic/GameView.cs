using System;
using System.Windows;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MonoGame.Framework.WpfInterop;
using MonoGame.Framework.WpfInterop.Input;

using ACViewer.Enum;
using ACViewer.View;

namespace ACViewer
{
    public class GameView : WpfGame
    {
        public static GameView Instance { get; set; }

        public WpfGraphicsDeviceService _graphicsDeviceManager { get; set; }
        public SpriteBatch SpriteBatch { get; set; }

        public WpfKeyboard _keyboard { get; set; }
        public WpfMouse _mouse { get; set; }

        public KeyboardState PrevKeyboardState { get; set; }
        public MouseState PrevMouseState { get; set; }

        public new Render.Render Render { get; set; }
        
        public static Camera Camera { get; set; }
        public static Camera PreviewCamera { get; set; }

        public Player Player { get; set; }

        public static WorldViewer WorldViewer { get; set; }
        public static MapViewer MapViewer { get; set; }
        public static ModelViewer ModelViewer { get; set; }
        public static TextureViewer TextureViewer { get; set; }
        public static ParticleViewer ParticleViewer { get; set; }
        public static WorldObjectViewer WorldObjectViewer { get; set; }

        private static ViewMode _viewMode { get; set; }
        private static ViewMode _previewMode { get; set; }
        private static ViewMode _lastActivePreviewMode { get; set; } = ViewMode.Model;

        // Track if mouse button is down in main view to prevent minimap teleportation during editing
        private bool _isMouseDownInMainView = false;

        public static ViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (_viewMode == value) return;

                _viewMode = value;

                if (_viewMode == ViewMode.Model || _viewMode == ViewMode.WorldObject)
                {
                    Camera.Position = new Vector3(-10, -10, 10);
                    Camera.Dir = Vector3.Normalize(-Camera.Position);
                    Camera.Speed = Camera.Model_Speed;
                    Camera.SetNearPlane(Camera.NearPlane_Model);
                }
                else if (_viewMode == ViewMode.Particle)
                {
                    Camera.InitParticle();
                }
                else if (_viewMode == ViewMode.World)
                {
                    Camera.SetNearPlane(Camera.NearPlane_World);
                }
            }
        }

        public static ViewMode PreviewMode
        {
            get => _previewMode;
            set
            {
                if (_previewMode == value) return;

                if (value != ViewMode.Undef)
                    _lastActivePreviewMode = value;

                _previewMode = value;

                if (_previewMode == ViewMode.Model || _previewMode == ViewMode.WorldObject)
                {
                    PreviewCamera.Speed = Camera.Model_Speed;
                    PreviewCamera.SetNearPlane(Camera.NearPlane_Model);
                }
                else if (_previewMode == ViewMode.Particle)
                {
                    PreviewCamera.InitParticle();
                }
            }
        }

        public static void TogglePreview()
        {
            if (PreviewMode != ViewMode.Undef)
                PreviewMode = ViewMode.Undef;
            else
                PreviewMode = _lastActivePreviewMode;
        }

        public static bool UseMSAA { get; set; } = true;

        public DateTime LastResizeEvent { get; set; }

        // text rendering
        public SpriteFont Font { get; set; }

        private Texture2D _whitePixel;
        private Texture2D Pixel
        {
            get
            {
                if (_whitePixel == null)
                {
                    _whitePixel = new Texture2D(GraphicsDevice, 1, 1);
                    _whitePixel.SetData(new[] { Color.White });
                }
                return _whitePixel;
            }
        }

        protected override void Initialize()
        {
            // must be initialized. required by Content loading and rendering (will add itself to the Services)
            // note that MonoGame requires this to be initialized in the constructor, while WpfInterop requires it to
            // be called inside Initialize (before base.Initialize())
            //var dummy = new DummyView();
            _graphicsDeviceManager = new WpfGraphicsDeviceService(this)
            {
                PreferMultiSampling = UseMSAA
            };

            SpriteBatch = new SpriteBatch(GraphicsDevice);

            // wpf and keyboard need reference to the host control in order to receive input
            // this means every WpfGame control will have it's own keyboard & mouse manager which will only react if the mouse is in the control
            _keyboard = new WpfKeyboard(this);
            _mouse = new WpfMouse(this);

            Instance = this;

            // must be called after the WpfGraphicsDeviceService instance was created
            base.Initialize();

            SizeChanged += new SizeChangedEventHandler(GameView_SizeChanged);
        }

        protected override void LoadContent()
        {
            base.LoadContent();

            Font = Content.Load<SpriteFont>("Fonts/Consolas");
        }

        public void PostInit()
        {
            InitPlayer();

            Render = new Render.Render();
            PreviewCamera = new Camera(this);

            WorldViewer = new WorldViewer();
            MapViewer = new MapViewer();
            ModelViewer = new ModelViewer();
            TextureViewer = new TextureViewer();
            ParticleViewer = new ParticleViewer();
            WorldObjectViewer = new WorldObjectViewer();

            ViewMode = ViewMode.World;
            // Only initialize to Undef if we don't have an active preview
            if (PreviewMode == ViewMode.Undef)
                PreviewMode = ViewMode.Undef;
        }

        public void InitPlayer()
        {
            Player = new Player();
        }

        public Viewport GetPipViewport(Viewport originalViewport)
        {
            int pipWidth = originalViewport.Width / 4;
            int pipHeight = originalViewport.Height / 4;
            return new Viewport(originalViewport.Width - pipWidth - 20, originalViewport.Height - pipHeight - 20, pipWidth, pipHeight);
        }

        public Viewport GetMiniMapViewport(Viewport originalViewport)
        {
            int miniWidth = originalViewport.Width / 8;
            int miniHeight = miniWidth; // Square
            return new Viewport(originalViewport.Width - miniWidth - 20, 20, miniWidth, miniHeight);
        }

        public bool IsMouseInViewport(int x, int y, Viewport viewport)
        {
            return x >= viewport.X && x <= viewport.X + viewport.Width &&
                   y >= viewport.Y && y <= viewport.Y + viewport.Height;
        }

        protected override void Update(GameTime time)
        {
            if (Render == null) return;

            // every update we can now query the keyboard & mouse for our WpfGame
            var keyboardState = _keyboard.GetState();
            var mouseState = _mouse.GetState();

            if (keyboardState.IsKeyDown(Keys.C) && !PrevKeyboardState.IsKeyDown(Keys.C))
            {
                // cancel all emitters in progress
                // this handles both ParticleViewer and ModelViewer
                Player?.PhysicsObj?.destroy_particle_manager();
            }

            /*if (keyboardState.IsKeyDown(Keys.L) && !PrevKeyboardState.IsKeyDown(Keys.L))
            {
                ViewMode = ViewMode.WorldObject;
                WorldObjectViewer.Instance.LoadModel(42809);
            }*/

            if (!_graphicsDeviceManager.PreferMultiSampling && UseMSAA && DateTime.Now - LastResizeEvent >= TimeSpan.FromSeconds(1))
            {
                _graphicsDeviceManager.PreferMultiSampling = true;
                _graphicsDeviceManager.ApplyChanges();
            }

            // Track mouse state in main view to prevent minimap teleportation during editing
            var mainViewport = GraphicsDevice.Viewport;
            var isMouseInMainView = mouseState.X >= 0 && mouseState.X < mainViewport.Width &&
                                   mouseState.Y >= 0 && mouseState.Y < mainViewport.Height &&
                                   !IsMouseInViewport(mouseState.X, mouseState.Y, GetMiniMapViewport(mainViewport));

            // Update the mouse down state in main view
            if (mouseState.LeftButton == ButtonState.Pressed && isMouseInMainView)
            {
                _isMouseDownInMainView = true;
            }
            else if (mouseState.LeftButton == ButtonState.Released)
            {
                _isMouseDownInMainView = false;
            }

            // Minimap click handling
            if (ViewMode != ViewMode.Map && MapViewer != null && MapViewer.WorldMap != null)
            {
                var miniViewport = GetMiniMapViewport(GraphicsDevice.Viewport);
                if (mouseState.LeftButton == ButtonState.Pressed && IsMouseInViewport(mouseState.X, mouseState.Y, miniViewport))
                {
                    // Only teleport if mouse was not pressed down in main view initially
                    // This prevents teleportation when dragging from main view to minimap while editing
                    if (!_isMouseDownInMainView)
                    {
                        float localX = mouseState.X - miniViewport.X;
                        float localY = mouseState.Y - miniViewport.Y;
                        // Dereth is ~48960m square
                        float worldX = (localX / miniViewport.Width) * 48960.0f;
                        float worldY = (1.0f - (localY / miniViewport.Height)) * 48960.0f;

                        uint lbx = (uint)Math.Clamp(worldX / 192.0f, 0, 254);
                        uint lby = (uint)Math.Clamp(worldY / 192.0f, 0, 254);
                        uint lbid = (lbx << 24) | (lby << 16) | 0xFFFF;

                        WorldViewer?.LoadLandblock(lbid, WorldViewer.WorldView ? 3u : 1u);
                    }
                }
            }

            // Update main view (keyboard + mouse)
            UpdateView(ViewMode, time, false);

            // Update preview if active (mouse only, within viewport)
            if (PreviewMode != ViewMode.Undef && PreviewCamera != null)
            {
                var pipViewport = GetPipViewport(GraphicsDevice.Viewport);
                PreviewCamera.UpdatePreview(time, pipViewport);
                UpdateView(PreviewMode, time, true);
            }

            PrevKeyboardState = keyboardState;
            PrevMouseState = mouseState;

            base.Update(time);
        }

        private void UpdateView(ViewMode mode, GameTime time, bool isPreview)
        {
            switch (mode)
            {
                case ViewMode.Texture:
                    TextureViewer?.Update(time);
                    break;
                case ViewMode.Model:
                    ModelViewer?.Update(time);
                    break;
                case ViewMode.World:
                    WorldViewer?.Update(time);
                    break;
                case ViewMode.Map:
                    MapViewer?.Update(time);
                    break;
                case ViewMode.Particle:
                    ParticleViewer?.Update(time);
                    break;
                case ViewMode.WorldObject:
                    WorldObjectViewer?.Update(time);
                    break;
            }
        }

        private void DrawView(ViewMode mode, GameTime time, bool isPreview)
        {
            switch (mode)
            {
                case ViewMode.Texture:
                    TextureViewer?.Draw(time, isPreview);
                    break;
                case ViewMode.Model:
                    ModelViewer?.Draw(time, isPreview);
                    break;
                case ViewMode.World:
                    WorldViewer?.Draw(time);
                    break;
                case ViewMode.Map:
                    MapViewer?.Draw(time, isPreview);
                    break;
                case ViewMode.Particle:
                    ParticleViewer?.Draw(time);
                    break;
                case ViewMode.WorldObject:
                    WorldObjectViewer?.Draw(time);
                    break;
            }
        }

        protected override void Draw(GameTime time)
        {
            if (Render == null) { GraphicsDevice.Clear(Color.Black); return; }

            var originalViewport = GraphicsDevice.Viewport;

            // 1. Draw main view
            DrawView(ViewMode, time, false);

            // 2. Draw preview (PiP Viewport - Bottom Right)
            if (PreviewMode != ViewMode.Undef)
            {
                var pipViewport = GetPipViewport(originalViewport);
                GraphicsDevice.Viewport = pipViewport;
                
                GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1.0f, 0);
                
                SpriteBatch.Begin();
                SpriteBatch.Draw(Pixel, new Rectangle(0, 0, pipViewport.Width, pipViewport.Height), Color.Black * 0.5f);
                SpriteBatch.End();

                DrawView(PreviewMode, time, true);
                
                GraphicsDevice.Viewport = originalViewport;

                SpriteBatch.Begin();
                var rect = new Rectangle(pipViewport.X - 2, pipViewport.Y - 2, pipViewport.Width + 4, pipViewport.Height + 4);
                DrawBorder(rect, 2, Color.White * 0.5f);
                SpriteBatch.End();
            }

            // 3. Draw Minimap (PiP Viewport - Top Right)
            if (ViewMode != ViewMode.Map && MainMenu.ShowMinimap && MapViewer != null && MapViewer.WorldMap != null && Camera != null) // Don't show minimap if we are already in map view
            {
                var miniViewport = GetMiniMapViewport(originalViewport);
                
                GraphicsDevice.Viewport = miniViewport;
                
                // Clear depth for minimap
                GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1.0f, 0);
                
                MapViewer.Draw(time, true);

                // Draw player location dot on the minimap
                var camPos = Camera.Position;
                // Dereth is ~48960m square
                float mapX = (camPos.X / 48960.0f) * miniViewport.Width;
                // Map Y is inverted in 2D (0 is top)
                float mapY = (1.0f - (camPos.Y / 48960.0f)) * miniViewport.Height;

                SpriteBatch.Begin();
                // Small 4x4 white dot
                SpriteBatch.Draw(Pixel, new Rectangle((int)mapX - 2, (int)mapY - 2, 4, 4), Color.White);
                SpriteBatch.End();

                GraphicsDevice.Viewport = originalViewport;

                // Border for minimap
                SpriteBatch.Begin();
                var rect = new Rectangle(miniViewport.X - 2, miniViewport.Y - 2, miniViewport.Width + 4, miniViewport.Height + 4);
                DrawBorder(rect, 2, Color.White * 0.5f);
                SpriteBatch.End();
            }

            base.Draw(time);
        }

        private void DrawBorder(Rectangle rect, int thickness, Color color)
        {
            SpriteBatch.Draw(Pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            SpriteBatch.Draw(Pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            SpriteBatch.Draw(Pixel, new Rectangle(rect.X + rect.Width - thickness, rect.Y, thickness, rect.Height), color);
            SpriteBatch.Draw(Pixel, new Rectangle(rect.X, rect.Y + rect.Height - thickness, rect.Width, thickness), color);
        }

        private void GameView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_graphicsDeviceManager.PreferMultiSampling)
            {
                _graphicsDeviceManager.PreferMultiSampling = false;
                _graphicsDeviceManager.ApplyChanges();

                LastResizeEvent = DateTime.Now;
            }
        }
    }
}
