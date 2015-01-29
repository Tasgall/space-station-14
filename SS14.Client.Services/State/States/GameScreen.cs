﻿using GorgonLibrary;
using GorgonLibrary.Graphics;
using GorgonLibrary.InputDevices;
using Lidgren.Network;
using SS14.Client.ClientWindow;
using SS14.Client.GameObjects;
using SS14.Client.Interfaces.GameTimer;
using SS14.Client.Interfaces.GOC;
using SS14.Client.Interfaces.Lighting;
using SS14.Client.Interfaces.Map;
using SS14.Client.Interfaces.Player;
using SS14.Client.Interfaces.Resource;
using SS14.Client.Interfaces.Serialization;
using SS14.Client.Interfaces.State;
using SS14.Client.Services.Helpers;
using SS14.Client.Services.Lighting;
using SS14.Client.Services.Tiles;
using SS14.Client.Services.UserInterface.Components;
using SS14.Client.Services.UserInterface.Inventory;
using SS14.Shared;
using SS14.Shared.GameObjects;
using SS14.Shared.GameStates;
using SS14.Shared.GO;
using SS14.Shared.IoC;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EntityManager = SS14.Client.GameObjects.EntityManager;
using Image = GorgonLibrary.Graphics.Image;

namespace SS14.Client.Services.State.States
{
    public class GameScreen : State, IState
    {
        #region Variables

        //UI Vars

        public bool BlendLightMap = true;
        public DateTime LastUpdate;
        public DateTime Now;
        public int ScreenHeightTiles = 12;
        public int ScreenWidthTiles = 15; // How many tiles around us do we draw?
        public string SpawnType;
        private RenderImage _baseTarget;
        private Sprite _baseTargetSprite;
        private Batch _decalBatch;
        private EntityManager _entityManager;
        private Batch _floorBatch;
        private Batch _gasBatch;
        private GaussianBlur _gaussianBlur;
        private RenderImage _lightTarget;
        private RenderImage _lightTargetIntermediate;
        private Sprite _lightTargetIntermediateSprite;
        private Sprite _lightTargetSprite;
        private float _realScreenHeightTiles;
        private float _realScreenWidthTiles;
        private bool _recalculateScene = true;
        private bool _redrawOverlay = true;
        private bool _redrawTiles = true;
        private List<RenderTarget> _cleanupList = new List<RenderTarget>();
        private List<Sprite> _cleanupSpriteList = new List<Sprite>();
        private List<ITile> _visibleTiles;

        private bool _showDebug; // show AABBs & Bounding Circles on Entities.
        private Batch _wallBatch;
        private Batch _wallTopsBatch;

        #region gameState stuff

        private readonly Dictionary<uint, GameState> _lastStates = new Dictionary<uint, GameState>();
        private uint _currentStateSequence; //We only ever want a newer state than the current one

        #endregion

        #region Mouse/Camera stuff

        public Vector2D MousePosScreen = Vector2D.Zero;
        public Vector2D MousePosWorld = Vector2D.Zero;

        #endregion

        #region UI Variables

        private Chatbox _gameChat;
        private HandsGui _handsGui;

        #endregion

        #region Lighting

        private RenderImage _composedSceneTarget;
        private RenderImage _overlayTarget;
        private RenderImage _sceneTarget;
        private RenderImage _tilesTarget;
        private bool bPlayerVision = true;
        private bool bFullVision = false;
        private FXShader finalBlendShader;
        private LightArea lightArea1024;
        private LightArea lightArea128;
        private LightArea lightArea256;
        private LightArea lightArea512;
        private FXShader lightBlendShader;
        private FXShader lightMapShader;
        private RenderImage playerOcclusionTarget;
        private ILight playerVision;
        private RenderImage _occluderDebugTarget;

        private QuadRenderer quadRenderer;
        private RenderImage screenShadows;
        private RenderImage shadowBlendIntermediate;
        private RenderImage shadowIntermediate;
        private ShadowMapResolver shadowMapResolver;
        private bool debugWallOccluders = false;
        private bool debugHitboxes = false;

        #endregion

        /// <summary>
        /// Center point of the current render window.
        /// </summary>
        private Vector2D WindowOrigin
        {
            get { return ClientWindowData.Singleton.ScreenOrigin; }
        }

        #endregion

        public GameScreen(IDictionary<Type, object> managers)
            : base(managers)
        {
        }

        #region Startup, Shutdown, Update

        public void Startup()
        {
            LastUpdate = DateTime.Now;
            Now = DateTime.Now;

            _cleanupList = new List<RenderTarget>();
            _cleanupSpriteList = new List<Sprite>();

            UserInterfaceManager.DisposeAllComponents();

            //Init serializer
            var serializer = IoCManager.Resolve<ISS14Serializer>();

            _entityManager = new EntityManager(NetworkManager);
            IoCManager.Resolve<IEntityManagerContainer>().EntityManager = _entityManager;

            MapManager.Init();

            NetworkManager.MessageArrived += NetworkManagerMessageArrived;

            NetworkManager.RequestMap();
            IoCManager.Resolve<IMapManager>().OnTileChanged += OnTileChanged;

            IoCManager.Resolve<IPlayerManager>().OnPlayerMove += OnPlayerMove;

            // TODO This should go somewhere else, there should be explicit session setup and teardown at some point.
            NetworkManager.SendClientName(ConfigurationManager.GetPlayerName());

            _baseTarget = new RenderImage("baseTarget", Gorgon.CurrentClippingViewport.Width,
                                          Gorgon.CurrentClippingViewport.Height, ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_baseTarget);
            _baseTargetSprite = new Sprite("baseTargetSprite", _baseTarget) {DepthWriteEnabled = false};
            _cleanupSpriteList.Add(_baseTargetSprite);
            
            _sceneTarget = new RenderImage("sceneTarget", Gorgon.CurrentClippingViewport.Width,
                                           Gorgon.CurrentClippingViewport.Height, ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_sceneTarget);
            _tilesTarget = new RenderImage("tilesTarget", Gorgon.CurrentClippingViewport.Width,
                                           Gorgon.CurrentClippingViewport.Height, ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_tilesTarget);

            _overlayTarget = new RenderImage("overlayTarget", Gorgon.CurrentClippingViewport.Width,
                                             Gorgon.CurrentClippingViewport.Height, ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_overlayTarget);
            _overlayTarget.SourceBlend = AlphaBlendOperation.SourceAlpha;
            _overlayTarget.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha;
            _overlayTarget.SourceBlendAlpha = AlphaBlendOperation.SourceAlpha;
            _overlayTarget.DestinationBlendAlpha = AlphaBlendOperation.InverseSourceAlpha;

            _composedSceneTarget = new RenderImage("composedSceneTarget", Gorgon.CurrentClippingViewport.Width,
                                                   Gorgon.CurrentClippingViewport.Height,
                                                   ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_composedSceneTarget);

            _lightTarget = new RenderImage("lightTarget", Gorgon.CurrentClippingViewport.Width,
                                           Gorgon.CurrentClippingViewport.Height, ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_lightTarget);
            _lightTargetSprite = new Sprite("lightTargetSprite", _lightTarget) { DepthWriteEnabled = false };
            _cleanupSpriteList.Add(_lightTargetSprite);
            _lightTargetIntermediate = new RenderImage("lightTargetIntermediate", Gorgon.CurrentClippingViewport.Width,
                                                       Gorgon.CurrentClippingViewport.Height,
                                                       ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_lightTargetIntermediate);
            _lightTargetIntermediateSprite = new Sprite("lightTargetIntermediateSprite", _lightTargetIntermediate)
                                                 {DepthWriteEnabled = false};
            _cleanupSpriteList.Add(_lightTargetIntermediateSprite);

            _gasBatch = new Batch("gasBatch", 1);
            _gasBatch.SourceBlend = AlphaBlendOperation.SourceAlpha;
            _gasBatch.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha;
            _gasBatch.SourceBlendAlpha = AlphaBlendOperation.SourceAlpha;
            _gasBatch.DestinationBlendAlpha = AlphaBlendOperation.InverseSourceAlpha;

            _wallTopsBatch = new Batch("wallTopsBatch", 1);
            _wallTopsBatch.SourceBlend = AlphaBlendOperation.SourceAlpha;
            _wallTopsBatch.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha;
            _wallTopsBatch.SourceBlendAlpha = AlphaBlendOperation.SourceAlpha;
            _wallTopsBatch.DestinationBlendAlpha = AlphaBlendOperation.InverseSourceAlpha;

            _decalBatch = new Batch("decalBatch", 1);
            _decalBatch.SourceBlend = AlphaBlendOperation.SourceAlpha;
            _decalBatch.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha;
            _decalBatch.SourceBlendAlpha = AlphaBlendOperation.SourceAlpha;
            _decalBatch.DestinationBlendAlpha = AlphaBlendOperation.InverseSourceAlpha;

            _floorBatch = new Batch("floorBatch", 1);
            _wallBatch = new Batch("wallBatch", 1);

            _gaussianBlur = new GaussianBlur(ResourceManager);

            _realScreenWidthTiles = (float) Gorgon.CurrentClippingViewport.Width/MapManager.GetTileSpacing();
            _realScreenHeightTiles = (float) Gorgon.CurrentClippingViewport.Height/MapManager.GetTileSpacing();

            //Init GUI components
            _gameChat = new Chatbox(ResourceManager, UserInterfaceManager, KeyBindingManager);
            _gameChat.TextSubmitted += ChatTextboxTextSubmitted;
            UserInterfaceManager.AddComponent(_gameChat);

            //UserInterfaceManager.AddComponent(new StatPanelComponent(ConfigurationManager.GetPlayerName(), PlayerManager, NetworkManager, ResourceManager));

            var statusBar = new StatusEffectBar(ResourceManager, PlayerManager);
            statusBar.Position = new Point(Gorgon.CurrentClippingViewport.Width - 800, 10);
            UserInterfaceManager.AddComponent(statusBar);

            var hotbar = new Hotbar(ResourceManager);
            hotbar.Position = new Point(5, Gorgon.CurrentClippingViewport.Height - hotbar.ClientArea.Height - 5);
            hotbar.Update(0);
            UserInterfaceManager.AddComponent(hotbar);

            #region Lighting

            quadRenderer = new QuadRenderer();
            quadRenderer.LoadContent();
            shadowMapResolver = new ShadowMapResolver(quadRenderer, ShadowmapSize.Size1024, ShadowmapSize.Size1024,
                                                      ResourceManager);
            shadowMapResolver.LoadContent();
            lightArea128 = new LightArea(ShadowmapSize.Size128);
            lightArea256 = new LightArea(ShadowmapSize.Size256);
            lightArea512 = new LightArea(ShadowmapSize.Size512);
            lightArea1024 = new LightArea(ShadowmapSize.Size1024);
            screenShadows = new RenderImage("screenShadows", Gorgon.CurrentClippingViewport.Width,
                                            Gorgon.CurrentClippingViewport.Height, ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(screenShadows);
            screenShadows.UseDepthBuffer = false;
            shadowIntermediate = new RenderImage("shadowIntermediate", Gorgon.CurrentClippingViewport.Width,
                                                 Gorgon.CurrentClippingViewport.Height,
                                                 ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(shadowIntermediate);
            shadowIntermediate.UseDepthBuffer = false;
            shadowBlendIntermediate = new RenderImage("shadowBlendIntermediate", Gorgon.CurrentClippingViewport.Width,
                                                      Gorgon.CurrentClippingViewport.Height,
                                                      ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(shadowBlendIntermediate);
            shadowBlendIntermediate.UseDepthBuffer = false;
            playerOcclusionTarget = new RenderImage("playerOcclusionTarget", Gorgon.CurrentClippingViewport.Width,
                                                    Gorgon.CurrentClippingViewport.Height,
                                                    ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(playerOcclusionTarget);
            playerOcclusionTarget.UseDepthBuffer = false;
            lightBlendShader = IoCManager.Resolve<IResourceManager>().GetShader("lightblend");
            finalBlendShader = IoCManager.Resolve<IResourceManager>().GetShader("finallight");
            lightMapShader = IoCManager.Resolve<IResourceManager>().GetShader("lightmap");

            playerVision = IoCManager.Resolve<ILightManager>().CreateLight();
            playerVision.SetColor(Color.Transparent);
            playerVision.SetRadius(1024);
            playerVision.Move(Vector2D.Zero);

            #endregion

            _handsGui = new HandsGui();
            _handsGui.Position = new Point(hotbar.Position.X + 5, hotbar.Position.Y + 7);
            UserInterfaceManager.AddComponent(_handsGui);

            var combo = new HumanComboGui(PlayerManager, NetworkManager, ResourceManager, UserInterfaceManager);
            combo.Update(0);
            combo.Position = new Point(hotbar.ClientArea.Right - combo.ClientArea.Width + 5,
                                       hotbar.Position.Y - combo.ClientArea.Height - 5);
            UserInterfaceManager.AddComponent(combo);

            var healthPanel = new HealthPanel();
            healthPanel.Position = new Point(hotbar.ClientArea.Right - 1, hotbar.Position.Y + 11);
            healthPanel.Update(0);
            UserInterfaceManager.AddComponent(healthPanel);

            var targetingUi = new TargetingGui();
            targetingUi.Update(0);
            targetingUi.Position = new Point(healthPanel.ClientArea.Right - 1,
                                             healthPanel.ClientArea.Bottom - targetingUi.ClientArea.Height);
            UserInterfaceManager.AddComponent(targetingUi);

            var inventoryButton = new ImageButton
                                      {
                                          ImageNormal = "button_inv",
                                          Position = new Point(hotbar.Position.X + 172, hotbar.Position.Y + 2)
                                      };
            inventoryButton.Update(0);
            inventoryButton.Clicked += inventoryButton_Clicked;
            UserInterfaceManager.AddComponent(inventoryButton);

            var statusButton = new ImageButton
                                   {
                                       ImageNormal = "button_status",
                                       Position =
                                           new Point(inventoryButton.ClientArea.Right, inventoryButton.Position.Y)
                                   };
            statusButton.Update(0);
            statusButton.Clicked += statusButton_Clicked;
            UserInterfaceManager.AddComponent(statusButton);

            var craftButton = new ImageButton
                                  {
                                      ImageNormal = "button_craft",
                                      Position = new Point(statusButton.ClientArea.Right, statusButton.Position.Y)
                                  };
            craftButton.Update(0);
            craftButton.Clicked += craftButton_Clicked;
            UserInterfaceManager.AddComponent(craftButton);

            var menuButton = new ImageButton
                                 {
                                     ImageNormal = "button_menu",
                                     Position = new Point(craftButton.ClientArea.Right, craftButton.Position.Y)
                                 };
            menuButton.Update(0);
            menuButton.Clicked += menuButton_Clicked;
            UserInterfaceManager.AddComponent(menuButton);
        }

        public void Shutdown()
        {
            IoCManager.Resolve<IPlayerManager>().Detach();
            if(Gorgon.IsInitialized)
            {
                _cleanupSpriteList.ForEach(s => s.Image = null);
                _cleanupSpriteList.Clear();
                _cleanupList.ForEach(t => {t.ForceRelease();t.Dispose();});
                _cleanupList.Clear();
            }
            shadowMapResolver.Dispose();
            _gaussianBlur.Dispose();
            _entityManager.Shutdown();
            MapManager.Shutdown();
            UserInterfaceManager.DisposeAllComponents(); //HerpDerp. This is probably bad. Should not remove them ALL.
            NetworkManager.MessageArrived -= NetworkManagerMessageArrived;
            IoCManager.Resolve<IMapManager>().OnTileChanged -= OnTileChanged;
            //RenderTargetCache.DestroyAll();
            //GC.Collect();
        }

        public void Update(FrameEventArgs e)
        {
            LastUpdate = Now;
            Now = DateTime.Now;
            IoCManager.Resolve<IGameTimer>().UpdateTime(e.FrameDeltaTime);
            _entityManager.ComponentManager.Update(e.FrameDeltaTime);
            _entityManager.Update(e.FrameDeltaTime);
            PlacementManager.Update(MousePosScreen, MapManager);
            PlayerManager.Update(e.FrameDeltaTime);
            if (PlayerManager != null && PlayerManager.ControlledEntity != null)
                ClientWindowData.Singleton.UpdateViewPort(
                    PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position);

            MousePosWorld = new Vector2D(MousePosScreen.X + WindowOrigin.X, MousePosScreen.Y + WindowOrigin.Y);
        }

        private void ResetRendertargets()
        {
            int w = Gorgon.CurrentClippingViewport.Width;
            int h = Gorgon.CurrentClippingViewport.Height;

            _baseTarget.Width = w;
            _baseTarget.Height = h;
            _sceneTarget.Width = w;
            _sceneTarget.Height = h;
            _tilesTarget.Width = w;
            _tilesTarget.Height = h;
            _overlayTarget.Width = w;
            _overlayTarget.Height = h;
            _composedSceneTarget.Width = w;
            _composedSceneTarget.Height = h;
            _lightTarget.Width = w;
            _lightTarget.Height = h;
            _lightTargetIntermediate.Width = w;
            _lightTargetIntermediate.Height = h;
            screenShadows.Width = w;
            screenShadows.Height = h;
            shadowIntermediate.Width = w;
            shadowIntermediate.Height = h;
            shadowBlendIntermediate.Width = w;
            shadowBlendIntermediate.Height = h;
            playerOcclusionTarget.Dispose();
            playerOcclusionTarget = new RenderImage("playerOcclusionTarget", Gorgon.CurrentClippingViewport.Width,
                                                    Gorgon.CurrentClippingViewport.Height,
                                                    ImageBufferFormats.BufferRGB888A8);
            playerOcclusionTarget.Width = w;
            playerOcclusionTarget.Height = h;
            //playerOcclusionTarget.DeviceReset();
            _gaussianBlur.Dispose();
            _gaussianBlur = new GaussianBlur(ResourceManager);
        }

        private void menuButton_Clicked(ImageButton sender)
        {
            UserInterfaceManager.DisposeAllComponents<MenuWindow>(); //Remove old ones.
            UserInterfaceManager.AddComponent(new MenuWindow()); //Create a new one.
        }

        private void craftButton_Clicked(ImageButton sender)
        {
            UserInterfaceManager.ComponentUpdate(GuiComponentType.ComboGui, ComboGuiMessage.ToggleShowPage, 3);
        }

        private void statusButton_Clicked(ImageButton sender)
        {
            UserInterfaceManager.ComponentUpdate(GuiComponentType.ComboGui, ComboGuiMessage.ToggleShowPage, 2);
        }

        private void inventoryButton_Clicked(ImageButton sender)
        {
            UserInterfaceManager.ComponentUpdate(GuiComponentType.ComboGui, ComboGuiMessage.ToggleShowPage, 1);
        }

        private void NetworkManagerMessageArrived(object sender, IncomingNetworkMessageArgs args)
        {
            NetIncomingMessage message = args.Message;
            if (message == null)
            {
                return;
            }
            switch (message.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    var statMsg = (NetConnectionStatus) message.ReadByte();
                    if (statMsg == NetConnectionStatus.Disconnected)
                    {
                        string disconnectMessage = message.ReadString();
                        UserInterfaceManager.AddComponent(new DisconnectedScreenBlocker(StateManager,
                                                                                        UserInterfaceManager,
                                                                                        ResourceManager,
                                                                                        disconnectMessage));
                    }
                    break;
                case NetIncomingMessageType.Data:
                    var messageType = (NetMessage) message.ReadByte();
                    switch (messageType)
                    {
                        case NetMessage.MapMessage:
                            MapManager.HandleNetworkMessage(message);
                            break;
                        case NetMessage.AtmosDisplayUpdate:
                            MapManager.HandleAtmosDisplayUpdate(message);
                            break;
                        case NetMessage.PlayerSessionMessage:
                            PlayerManager.HandleNetworkMessage(message);
                            break;
                        case NetMessage.PlayerUiMessage:
                            UserInterfaceManager.HandleNetMessage(message);
                            break;
                        case NetMessage.PlacementManagerMessage:
                            PlacementManager.HandleNetMessage(message);
                            break;
                        case NetMessage.ChatMessage:
                            HandleChatMessage(message);
                            break;
                        case NetMessage.EntityMessage:
                            _entityManager.HandleEntityNetworkMessage(message);
                            break;
                        case NetMessage.RequestAdminLogin:
                            HandleAdminMessage(messageType, message);
                            break;
                        case NetMessage.RequestAdminPlayerlist:
                            HandleAdminMessage(messageType, message);
                            break;
                        case NetMessage.RequestBanList:
                            HandleAdminMessage(messageType, message);
                            break;
                        case NetMessage.StateUpdate:
                            HandleStateUpdate(message);
                            break;
                        case NetMessage.FullState:
                            HandleFullState(message);
                            break;
                    }
                    break;
            }
        }

        public void HandleAdminMessage(NetMessage adminMsgType, NetIncomingMessage messageBody)
        {
            switch (adminMsgType)
            {
                case NetMessage.RequestAdminLogin:
                    UserInterfaceManager.DisposeAllComponents<AdminPasswordDialog>(); //Remove old ones.
                    UserInterfaceManager.AddComponent(new AdminPasswordDialog(new Size(200, 50), NetworkManager,
                                                                              ResourceManager)); //Create a new one.
                    break;
                case NetMessage.RequestAdminPlayerlist:
                    UserInterfaceManager.DisposeAllComponents<AdminPlayerPanel>();
                    UserInterfaceManager.AddComponent(new AdminPlayerPanel(new Size(600, 200), NetworkManager,
                                                                           ResourceManager, messageBody));
                    break;
                case NetMessage.RequestBanList:
                    var banList = new Banlist();
                    int entriesCount = messageBody.ReadInt32();
                    for (int i = 0; i < entriesCount; i++)
                    {
                        string ipAddress = messageBody.ReadString();
                        string reason = messageBody.ReadString();
                        bool tempBan = messageBody.ReadBoolean();
                        uint minutesLeft = messageBody.ReadUInt32();
                        var entry = new BanEntry
                                        {
                                            ip = ipAddress,
                                            reason = reason,
                                            tempBan = tempBan,
                                            expiresAt = DateTime.Now.AddMinutes(minutesLeft)
                                        };
                        banList.List.Add(entry);
                    }
                    UserInterfaceManager.DisposeAllComponents<AdminUnbanPanel>();
                    UserInterfaceManager.AddComponent(new AdminUnbanPanel(new Size(620, 200), banList, NetworkManager,
                                                                          ResourceManager));
                    break;
            }
        }

        #endregion

        #region IState Members

        public void GorgonRender(FrameEventArgs e)
        {
            Gorgon.CurrentRenderTarget = _baseTarget;

            _baseTarget.Clear(Color.Black);
            Gorgon.Screen.Clear(Color.Black);

            //Gorgon.Screen.DefaultView.Left = 400;
            //Gorgon.Screen.DefaultView.Top = 400;

            //CalculateAllLights();

            if (PlayerManager.ControlledEntity != null)
            {

                // Get nearby lights
                ILight[] lights =
                    IoCManager.Resolve<ILightManager>().LightsIntersectingRect(ClientWindowData.Singleton.ViewPort);

                // Render the lightmap
                RenderLightMap(lights);
                CalculateSceneBatches(ClientWindowData.Singleton.ViewPort);

                if (_redrawTiles)
                {
                    //Set rendertarget to draw the rest of the scene
                    Gorgon.CurrentRenderTarget = _tilesTarget;
                    Gorgon.CurrentRenderTarget.Clear(Color.Black);

                    if (_floorBatch.Count > 0)
                        _floorBatch.Draw();

                    if (_wallBatch.Count > 0)
                        _wallBatch.Draw();

                    _redrawTiles = false;
                }


                Gorgon.CurrentRenderTarget = _sceneTarget;
                _sceneTarget.Clear(Color.Black);

                _tilesTarget.Image.Blit(0, 0, _tilesTarget.Width, _tilesTarget.Height, Color.White, BlitterSizeMode.Crop);

                //ComponentManager.Singleton.Render(0, ClientWindowData.Singleton.ViewPort);
                RenderComponents(e.FrameDeltaTime, ClientWindowData.Singleton.ViewPort);

                if (_redrawOverlay)
                {
                    Gorgon.CurrentRenderTarget = _overlayTarget;
                    _overlayTarget.Clear(Color.Transparent);

                    // Render decal batch

                    if (_decalBatch.Count > 0)
                        _decalBatch.Draw();

                    if (_wallTopsBatch.Count > 0)
                        _wallTopsBatch.Draw();

                    if (_gasBatch.Count > 0)
                        _gasBatch.Draw();

                    Gorgon.CurrentRenderTarget = _sceneTarget;
                    _redrawOverlay = false;
                }

                _overlayTarget.Blit();

                LightScene();



                RenderDebug();
                //Render the placement manager shit
                PlacementManager.Render();
            }
        }

        private void RenderDebug()
        {   
            if(debugWallOccluders)
                _occluderDebugTarget.Blit(0,0,_occluderDebugTarget.Width, _occluderDebugTarget.Height, Color.White, BlitterSizeMode.Crop);

            if (debugHitboxes) {
                var corner = ClientWindowData.Singleton.ScreenOrigin;

                var colliders =
                    _entityManager.ComponentManager.GetComponents(ComponentFamily.Collider)
                    .OfType<ColliderComponent>()
                    .Select(c => new { Color = c.DebugColor, AABB = c.WorldAABB })
                    .Where(c => !c.AABB.IsEmpty && c.AABB.IntersectsWith(ClientWindowData.Singleton.ViewPort));

                var collidables =
                    _entityManager.ComponentManager.GetComponents(ComponentFamily.Collidable)
                    .OfType<CollidableComponent>()
                    .Select(c => new { Color = c.DebugColor, AABB = c.AABB })
                    .Where(c => !c.AABB.IsEmpty && c.AABB.IntersectsWith(ClientWindowData.Singleton.ViewPort));

                var destAbo = _baseTarget.DestinationBlend;
                var srcAbo = _baseTarget.SourceBlend;
                _baseTarget.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha;
                _baseTarget.SourceBlend = AlphaBlendOperation.SourceAlpha;

                foreach (var hitbox in colliders.Concat(collidables)) {
                    _baseTarget.FilledRectangle(
                        hitbox.AABB.Left - corner.X, hitbox.AABB.Top - corner.Y,
                        hitbox.AABB.Width, hitbox.AABB.Height, Color.FromArgb(64, hitbox.Color));
                    _baseTarget.Rectangle(
                        hitbox.AABB.Left - corner.X, hitbox.AABB.Top - corner.Y,
                        hitbox.AABB.Width, hitbox.AABB.Height, Color.FromArgb(128, hitbox.Color));
                }

                _baseTarget.DestinationBlend = destAbo;
                _baseTarget.SourceBlend = srcAbo;
            
            }
        }

        public void FormResize()
        {
            Gorgon.CurrentClippingViewport = new Viewport(0, 0, Gorgon.CurrentClippingViewport.Width,
                                                          Gorgon.CurrentClippingViewport.Height);
            ClientWindowData.Singleton.UpdateViewPort(
                PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position);
            UserInterfaceManager.ResizeComponents();
            ResetRendertargets();
            IoCManager.Resolve<ILightManager>().RecalculateLights();
            RecalculateScene();
        }

        #endregion

        #region Input

        public void KeyDown(KeyboardInputEventArgs e)
        {
            if (UserInterfaceManager.KeyDown(e)) //KeyDown returns true if the click is handled by the ui component.
                return;

            if (e.Key == KeyboardKeys.F1)
            {
                Gorgon.FrameStatsVisible = !Gorgon.FrameStatsVisible;
            }
            if (e.Key == KeyboardKeys.F2)
            {
                _showDebug = !_showDebug;
            }
            if (e.Key == KeyboardKeys.F3)
            {
                ToggleOccluderDebug();
            }
            if (e.Key == KeyboardKeys.F4)
            {
                debugHitboxes = !debugHitboxes;
            }
            if (e.Key == KeyboardKeys.F5)
            {
                PlayerManager.SendVerb("save", 0);
            }
            if (e.Key == KeyboardKeys.F6)
            {
                bFullVision = !bFullVision;
            }
            if (e.Key == KeyboardKeys.F7)
            {
                bPlayerVision = !bPlayerVision;
            }
            if (e.Key == KeyboardKeys.F8)
            {
                NetOutgoingMessage message = NetworkManager.CreateMessage();
                message.Write((byte) NetMessage.ForceRestart);
                NetworkManager.SendMessage(message, NetDeliveryMethod.ReliableUnordered);
            }
            if (e.Key == KeyboardKeys.Escape)
            {
                UserInterfaceManager.DisposeAllComponents<MenuWindow>(); //Remove old ones.
                UserInterfaceManager.AddComponent(new MenuWindow()); //Create a new one.
            }
            if (e.Key == KeyboardKeys.F9)
            {
                UserInterfaceManager.ToggleMoveMode();
            }
            if (e.Key == KeyboardKeys.F10)
            {
                UserInterfaceManager.DisposeAllComponents<TileSpawnPanel>(); //Remove old ones.
                UserInterfaceManager.AddComponent(new TileSpawnPanel(new Size(350, 410), ResourceManager,
                                                                     PlacementManager)); //Create a new one.
            }
            if (e.Key == KeyboardKeys.F11)
            {
                UserInterfaceManager.DisposeAllComponents<EntitySpawnPanel>(); //Remove old ones.
                UserInterfaceManager.AddComponent(new EntitySpawnPanel(new Size(350, 410), ResourceManager,
                                                                       PlacementManager)); //Create a new one.
            }
            if (e.Key == KeyboardKeys.F12)
            {
                UserInterfaceManager.DisposeAllComponents<PlayerActionsWindow>(); //Remove old ones.
                var actComp =
                    (PlayerActionComp) PlayerManager.ControlledEntity.GetComponent(ComponentFamily.PlayerActions);
                if (actComp != null)
                    UserInterfaceManager.AddComponent(new PlayerActionsWindow(new Size(150, 150), ResourceManager,
                                                                              actComp)); //Create a new one.
            }

            PlayerManager.KeyDown(e.Key);
        }

        public void KeyUp(KeyboardInputEventArgs e)
        {
            PlayerManager.KeyUp(e.Key);
        }

        public void MouseUp(MouseInputEventArgs e)
        {
            UserInterfaceManager.MouseUp(e);
        }

        public void MouseDown(MouseInputEventArgs e)
        {
            if (PlayerManager.ControlledEntity == null)
                return;

            if (UserInterfaceManager.MouseDown(e))
                // MouseDown returns true if the click is handled by the ui component.
                return;

            if (PlacementManager.IsActive && !PlacementManager.Eraser)
            {
                switch (e.Buttons)
                {
                    case MouseButtons.Left:
                        PlacementManager.HandlePlacement();
                        return;
                    case MouseButtons.Right:
                        PlacementManager.Clear();
                        return;
                    case MouseButtons.Middle:
                        PlacementManager.Rotate();
                        return;
                }
            }

            #region Object clicking

            // Convert our click from screen -> world coordinates
            //Vector2D worldPosition = new Vector2D(e.Position.X + xTopLeft, e.Position.Y + yTopLeft);
            // A bounding box for our click
            var mouseAABB = new RectangleF(MousePosWorld.X, MousePosWorld.Y, 1, 1);
            float checkDistance = MapManager.GetTileSpacing()*1.5f;
            // Find all the entities near us we could have clicked
            Entity[] entities =
                ((EntityManager) IoCManager.Resolve<IEntityManagerContainer>().EntityManager).GetEntitiesInRange(
                    PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position,
                    checkDistance);

            // See which one our click AABB intersected with
            var clickedEntities = new List<ClickData>();
            var clickedWorldPoint = new PointF(mouseAABB.X, mouseAABB.Y);
            foreach (Entity entity in entities)
            {
                var clickable = (ClickableComponent) entity.GetComponent(ComponentFamily.Click);
                if (clickable == null) continue;
                int drawdepthofclicked;
                if (clickable.CheckClick(clickedWorldPoint, out drawdepthofclicked))
                    clickedEntities.Add(new ClickData(entity, drawdepthofclicked));
            }

            if (clickedEntities.Any())
            {
                //var entToClick = (from cd in clickedEntities                       //Treat mobs and their clothes as on the same level as ground placeables (windows, doors)
                //                  orderby (cd.Drawdepth == (int)DrawDepth.MobBase ||//This is a workaround to make both windows etc. and objects that rely on layers (objects on tables) work.
                //                            cd.Drawdepth == (int)DrawDepth.MobOverAccessoryLayer ||
                //                            cd.Drawdepth == (int)DrawDepth.MobOverClothingLayer ||
                //                            cd.Drawdepth == (int)DrawDepth.MobUnderAccessoryLayer ||
                //                            cd.Drawdepth == (int)DrawDepth.MobUnderClothingLayer
                //                   ? (int)DrawDepth.FloorPlaceable : cd.Drawdepth) ascending, cd.Clicked.Position.Y ascending
                //                  select cd.Clicked).Last();

                Entity entToClick = (from cd in clickedEntities
                                     orderby cd.Drawdepth ascending ,
                                         cd.Clicked.GetComponent<TransformComponent>(ComponentFamily.Transform).Position
                                         .Y ascending
                                     select cd.Clicked).Last();

                if (PlacementManager.Eraser && PlacementManager.IsActive)
                {
                    PlacementManager.HandleDeletion(entToClick);
                    return;
                }

                switch (e.Buttons)
                {
                    case MouseButtons.Left:
                        {
                            if (UserInterfaceManager.currentTargetingAction != null &&
                                (UserInterfaceManager.currentTargetingAction.TargetType == PlayerActionTargetType.Any ||
                                 UserInterfaceManager.currentTargetingAction.TargetType == PlayerActionTargetType.Other))
                                UserInterfaceManager.SelectTarget(entToClick);
                            else
                            {
                                var c = (ClickableComponent) entToClick.GetComponent(ComponentFamily.Click);
                                c.DispatchClick(PlayerManager.ControlledEntity.Uid);
                            }
                        }
                        break;

                    case MouseButtons.Right:
                        if (UserInterfaceManager.currentTargetingAction != null)
                            UserInterfaceManager.CancelTargeting();
                        else
                            UserInterfaceManager.AddComponent(new ContextMenu(entToClick, MousePosScreen,
                                                                              ResourceManager, UserInterfaceManager));
                        break;

                    case MouseButtons.Middle:
                        UserInterfaceManager.DisposeAllComponents<PropEditWindow>();
                        UserInterfaceManager.AddComponent(new PropEditWindow(new Size(400, 400), ResourceManager,
                                                                             entToClick));
                        break;
                }
            }
            else
            {
                switch (e.Buttons)
                {
                    case MouseButtons.Left:
                        {
                            if (UserInterfaceManager.currentTargetingAction != null &&
                                UserInterfaceManager.currentTargetingAction.TargetType == PlayerActionTargetType.Point)
                            {
                                UserInterfaceManager.SelectTarget(new PointF(MousePosWorld.X, MousePosWorld.Y));
                            }
                            else
                            {
                                /*Point clickedPoint = MapManager.GetTileArrayPositionFromWorldPosition(MousePosWorld);
                                if (clickedPoint.X > 0 && clickedPoint.Y > 0)
                                {
                                    NetOutgoingMessage message = NetworkManager.CreateMessage();
                                    message.Write((byte) NetMessage.MapMessage);
                                    message.Write((byte) MapMessage.TurfClick);
                                    message.Write((short) clickedPoint.X);
                                    message.Write((short) clickedPoint.Y);
                                    NetworkManager.SendMessage(message, NetDeliveryMethod.ReliableUnordered);
                                }*/
                            }
                            break;
                        }
                    case MouseButtons.Right:
                        {
                            if (UserInterfaceManager.currentTargetingAction != null)
                                UserInterfaceManager.CancelTargeting();
                            break;
                        }
                }
            }

            #endregion
        }

        public void MouseMove(MouseInputEventArgs e)
        {
            float distanceToPrev = (MousePosScreen - new Vector2D(e.Position.X, e.Position.Y)).Length;
            MousePosScreen = new Vector2D(e.Position.X, e.Position.Y);
            MousePosWorld = new Vector2D(e.Position.X + WindowOrigin.X, e.Position.Y + WindowOrigin.Y);
            UserInterfaceManager.MouseMove(e);
        }

        public void MouseWheelMove(MouseInputEventArgs e)
        {
            UserInterfaceManager.MouseWheelMove(e);
        }

        #endregion

        private void HandleChatMessage(NetIncomingMessage msg)
        {
            var channel = (ChatChannel) msg.ReadByte();
            string text = msg.ReadString();
            int entityId = msg.ReadInt32();
            string message;
            switch (channel)
            {
                    /*case ChatChannel.Emote:
                    message = _entityManager.GetEntity(entityId).Name + " " + text;
                    break;
                case ChatChannel.Damage:
                    message = text;
                    break; //Formatting is handled by the server. */
                case ChatChannel.Ingame:
                case ChatChannel.Server:
                case ChatChannel.OOC:
                case ChatChannel.Radio:
                    message = "[" + channel + "] " + text;
                    break;
                default:
                    message = text;
                    break;
            }
            _gameChat.AddLine(message, channel);
            if (entityId > 0)
            {
                Entity a = IoCManager.Resolve<IEntityManagerContainer>().EntityManager.GetEntity(entityId);
                if (a != null)
                {
                    a.SendMessage(this, ComponentMessageType.EntitySaidSomething, channel, text);
                }
            }
        }

        private void ChatTextboxTextSubmitted(Chatbox chatbox, string text)
        {
            SendChatMessage(text);
        }

        private void SendChatMessage(string text)
        {
            NetOutgoingMessage message = NetworkManager.CreateMessage();
            message.Write((byte) NetMessage.ChatMessage);
            message.Write((byte) ChatChannel.Player);
            message.Write(text);
            NetworkManager.SendMessage(message, NetDeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// HandleStateUpdate
        /// 
        /// Recieves a state update message and unpacks the delicious GameStateDelta hidden inside
        /// Then it applies the gamestatedelta to a past state to form: a full game state!
        /// </summary>
        /// <param name="message">incoming state update message</param>
        private void HandleStateUpdate(NetIncomingMessage message)
        {
            //Read the delta from the message
            GameStateDelta delta = GameStateDelta.ReadDelta(message);

            if (!_lastStates.ContainsKey(delta.FromSequence)) // Drop messages that reference a state that we don't have
                return; //TODO request full state here?

            //Acknowledge reciept before we do too much more shit -- ack as quickly as possible
            SendStateAck(delta.Sequence);

            //Grab the 'from' state
            GameState fromState = _lastStates[delta.FromSequence];
            //Apply the delta
            GameState newState = fromState + delta;
            newState.GameTime = IoCManager.Resolve<IGameTimer>().CurrentTime;

            // Go ahead and store it even if our current state is newer than this one, because
            // a newer state delta may later reference this one.
            _lastStates[delta.Sequence] = newState;

            if (delta.Sequence > _currentStateSequence)
                _currentStateSequence = delta.Sequence;

            ApplyCurrentGameState();

            //Dump states that have passed out of being relevant
            CullOldStates(delta.FromSequence);
        }

        /// <summary>
        /// CullOldStates
        /// 
        /// Deletes states that are no longer relevant
        /// </summary>
        /// <param name="sequence">state sequence number</param>
        private void CullOldStates(uint sequence)
        {
            foreach (uint v in _lastStates.Keys.Where(v => v < sequence).ToList())
                _lastStates.Remove(v);
        }

        /// <summary>
        /// HandleFullState
        /// 
        /// Handles full gamestates - for initializing.
        /// </summary>
        /// <param name="message">incoming full state message</param>
        private void HandleFullState(NetIncomingMessage message)
        {
            GameState newState = GameState.ReadStateMessage(message);
            newState.GameTime = IoCManager.Resolve<IGameTimer>().CurrentTime;
            SendStateAck(newState.Sequence);

            //Store the new state
            _lastStates[newState.Sequence] = newState;
            _currentStateSequence = newState.Sequence;
            ApplyCurrentGameState();
        }

        private void ApplyCurrentGameState()
        {
            GameState currentState = _lastStates[_currentStateSequence];
            _entityManager.ApplyEntityStates(currentState.EntityStates, currentState.GameTime);
            PlayerManager.ApplyPlayerStates(currentState.PlayerStates);
        }

        /// <summary>
        /// SendStateAck
        /// 
        /// Acknowledge a game state being recieved
        /// </summary>
        /// <param name="sequence">State sequence number</param>
        private void SendStateAck(uint sequence)
        {
            NetOutgoingMessage message = NetworkManager.CreateMessage();
            message.Write((byte) NetMessage.StateAck);
            message.Write(sequence);
            NetworkManager.SendMessage(message, NetDeliveryMethod.Unreliable);
        }

        private void ToggleOccluderDebug()
        {
            if(debugWallOccluders)
            {
                debugWallOccluders = false;
                _occluderDebugTarget.Dispose();
                _occluderDebugTarget = null;
            }
            else
            {
                debugWallOccluders = true;
                _occluderDebugTarget = new RenderImage("OccluderDebugTarget", Gorgon.Screen.Width, Gorgon.Screen.Height, ImageBufferFormats.BufferRGB888A8);
            }
        }

        /// <summary>
        /// Render the renderables
        /// </summary>
        /// <param name="frametime">time since the last frame was rendered.</param>
        private void RenderComponents(float frameTime, RectangleF viewPort)
        {
            IEnumerable<Component> components = _entityManager.ComponentManager.GetComponents(ComponentFamily.Renderable)
                .Union(_entityManager.ComponentManager.GetComponents(ComponentFamily.Particles));

            IEnumerable<IRenderableComponent> floorRenderables = from IRenderableComponent c in components
                                                                 orderby c.Bottom ascending , c.DrawDepth ascending
                                                                 where c.DrawDepth < DrawDepth.MobBase
                                                                 select c;

            RenderList(new Vector2D(viewPort.Left, viewPort.Top), new Vector2D(viewPort.Right, viewPort.Bottom),
                       floorRenderables);

            IEnumerable<IRenderableComponent> largeRenderables = from IRenderableComponent c in components
                                                                 orderby c.Bottom ascending
                                                                 where c.DrawDepth >= DrawDepth.MobBase &&
                                                                       c.DrawDepth < DrawDepth.WallTops
                                                                 select c;

            RenderList(new Vector2D(viewPort.Left, viewPort.Top), new Vector2D(viewPort.Right, viewPort.Bottom),
                       largeRenderables);

            IEnumerable<IRenderableComponent> ceilingRenderables = from IRenderableComponent c in components
                                                                   orderby c.Bottom ascending , c.DrawDepth ascending
                                                                   where c.DrawDepth >= DrawDepth.WallTops
                                                                   select c;

            RenderList(new Vector2D(viewPort.Left, viewPort.Top), new Vector2D(viewPort.Right, viewPort.Bottom),
                       ceilingRenderables);
        }

        private void RenderList(Vector2D topleft, Vector2D bottomright, IEnumerable<IRenderableComponent> renderables)
        {
            foreach (IRenderableComponent component in renderables)
            {
                if (component is SpriteComponent)
                {
                    //Slaved components are drawn by their master
                    var c = component as SpriteComponent;
                    if (c.IsSlaved())
                        continue;
                }
                component.Render(topleft, bottomright);
            }
        }

        private void CalculateAllLights()
        {
            foreach (
                ILight l in IoCManager.Resolve<ILightManager>().GetLights().Where(l => l.LightArea.Calculated == false))
            {
                CalculateLightArea(l);
            }
        }

        private void CalculateSceneBatches(RectangleF vision)
        {
            if (!_recalculateScene)
                return;

            // Render the player sightline occluder
            RenderPlayerVisionMap();

            //Blur the player vision map
            BlurPlayerVision();

            _decalBatch.Clear();
            _wallTopsBatch.Clear();
            _floorBatch.Clear();
            _wallBatch.Clear();
            _gasBatch.Clear();

            DrawTiles(vision);
            _recalculateScene = false;
            _redrawTiles = true;
            _redrawOverlay = true;
        }

        public void RecalculateScene()
        {
            _recalculateScene = true;
        }

        private void BlurShadowMap()
        {
            _gaussianBlur.SetRadius(11);
            _gaussianBlur.SetAmount(2);
            _gaussianBlur.SetSize(new Size(screenShadows.Width, screenShadows.Height));
            _gaussianBlur.PerformGaussianBlur(screenShadows);
        }

        private void BlurPlayerVision()
        {
            _gaussianBlur.SetRadius(11);
            _gaussianBlur.SetAmount(2);
            _gaussianBlur.SetSize(new Size(playerOcclusionTarget.Width, playerOcclusionTarget.Height));
            _gaussianBlur.PerformGaussianBlur(playerOcclusionTarget);
        }

        private void LightScene()
        {
            //Blur the light/shadow map
            BlurShadowMap();

            //Render the scene and lights together to compose the lit scene
            Gorgon.CurrentRenderTarget = _composedSceneTarget;
            Gorgon.CurrentRenderTarget.Clear(Color.Black);
            Gorgon.CurrentShader = finalBlendShader.Techniques["FinalLightBlend"];
            finalBlendShader.Parameters["PlayerViewTexture"].SetValue(playerOcclusionTarget);
            Sprite outofview = IoCManager.Resolve<IResourceManager>().GetSprite("outofview");
            finalBlendShader.Parameters["OutOfViewTexture"].SetValue(outofview.Image);
            float texratiox = Gorgon.CurrentClippingViewport.Width/outofview.Width;
            float texratioy = Gorgon.CurrentClippingViewport.Height/outofview.Height;
            var maskProps = new Vector4D(texratiox, texratioy, 0, 0);
            finalBlendShader.Parameters["MaskProps"].SetValue(maskProps);
            finalBlendShader.Parameters["LightTexture"].SetValue(screenShadows);
            finalBlendShader.Parameters["SceneTexture"].SetValue(_sceneTarget);
            finalBlendShader.Parameters["AmbientLight"].SetValue(new Vector4D(.05f, .05f, 0.05f, 1));
            screenShadows.Image.Blit(0, 0, screenShadows.Width, screenShadows.Height, Color.White, BlitterSizeMode.Crop);

            // Blit the shadow image on top of the screen
            Gorgon.CurrentShader = null;
            Gorgon.CurrentRenderTarget = null;

            
            playerOcclusionTarget.Blit(0,0, screenShadows.Width, screenShadows.Height, Color.White, BlitterSizeMode.Crop);
            PlayerPostProcess();

            _composedSceneTarget.Image.Blit(0, 0, Gorgon.CurrentClippingViewport.Width,
                                            Gorgon.CurrentClippingViewport.Height, Color.White, BlitterSizeMode.Crop);
            //screenShadows.Blit(0,0);
            //playerOcclusionTarget.Blit(0,0);
        }

        private void PlayerPostProcess()
        {
            PlayerManager.ApplyEffects(_composedSceneTarget);
        }

        private void OnPlayerMove(object sender, VectorEventArgs args)
        {
            //Recalculate scene batches for drawing.
            RecalculateScene();
        }

        #region Lighting

        /// <summary>
        /// Renders a set of lights into a single lightmap.
        /// If a light hasn't been prerendered yet, it renders that light.
        /// </summary>
        /// <param name="lights">Array of lights</param>
        private void RenderLightMap(ILight[] lights)
        {
            //Step 1 - Calculate lights that haven't been calculated yet or need refreshing
            foreach (ILight l in lights.Where(l => l.LightArea.Calculated == false))
            {
                if (l.LightState != LightState.On)
                    continue;
                //Render the light area to its own target.
                CalculateLightArea(l);
            }

            //Step 2 - Set up the render targets for the composite lighting.
            RenderImage source = screenShadows;
            source.Clear(Color.FromArgb(0, 0, 0, 0));
            RenderImage destination = shadowIntermediate;
            Gorgon.CurrentRenderTarget = destination;
            RenderImage copy;

            //Reset the shader and render target
            Gorgon.CurrentShader = lightMapShader.Techniques["PreLightBlend"];

            var lightTextures = new List<Image>();
            var colors = new List<Vector4D>();
            var positions = new List<Vector4D>();

            //Step 3 - Blend all the lights!
            foreach (ILight l in lights)
            {
                //Skip off or broken lights (TODO code broken light states)
                if (l.LightState != LightState.On)
                    continue;

                // LIGHT BLEND STAGE 1 - SIZING -- copys the light texture to a full screen rendertarget
                var area = (LightArea) l.LightArea;

                Vector2D blitPos;
                //Set the drawing position.
                blitPos = ClientWindowData.WorldToScreen(area.LightPosition - area.LightAreaSize*0.5f);

                //Set shader parameters
                var LightPositionData = new Vector4D(blitPos.X/source.Width,
                                                     blitPos.Y/source.Height,
                                                     (float) source.Width/area.renderTarget.Width,
                                                     (float) source.Height/area.renderTarget.Height);
                lightTextures.Add(area.renderTarget.Image);
                colors.Add(l.GetColorVec());
                positions.Add(LightPositionData);
            }
            int i = 0;
            int num_lights = 6;
            bool draw = false;
            bool fill = false;
            Image black = IoCManager.Resolve<IResourceManager>().GetSprite("black5x5").Image;
            var r_img = new Image[num_lights];
            var r_col = new Vector4D[num_lights];
            var r_pos = new Vector4D[num_lights];
            do
            {
                if (fill)
                {
                    for (int j = i; j < num_lights - 1; j++)
                    {
                        r_img[j] = black;
                        r_col[j] = Vector4D.Zero;
                        r_pos[j] = new Vector4D(0, 0, 1, 1);
                        j++;
                    }
                    i = num_lights;
                    draw = true;
                    fill = false;
                }
                if (draw)
                {
                    Gorgon.CurrentRenderTarget = destination;

                    lightMapShader.Parameters["LightPosData"].SetValue(r_pos);
                    lightMapShader.Parameters["Colors"].SetValue(r_col);
                    lightMapShader.Parameters["light1"].SetValue(r_img[0]);
                    lightMapShader.Parameters["light2"].SetValue(r_img[1]);
                    lightMapShader.Parameters["light3"].SetValue(r_img[2]);
                    lightMapShader.Parameters["light4"].SetValue(r_img[3]);
                    lightMapShader.Parameters["light5"].SetValue(r_img[4]);
                    lightMapShader.Parameters["light6"].SetValue(r_img[5]);
                    lightMapShader.Parameters["SceneTexture"].SetValue(source.Image);

                    source.Image.Blit(0, 0, screenShadows.Width, screenShadows.Height, Color.White, BlitterSizeMode.Crop);
                    // Blit the shadow image on top of the screen

                    //Swap rendertargets to set up for the next light
                    copy = source;
                    source = destination;
                    destination = copy;
                    i = 0;
                    draw = false;
                    r_img = new Image[num_lights];
                    r_col = new Vector4D[num_lights];
                    r_pos = new Vector4D[num_lights];
                }
                if (lightTextures.Count > 0)
                {
                    Image l = lightTextures[0];
                    lightTextures.RemoveAt(0);
                    r_img[i] = l;
                    r_col[i] = colors[0];
                    colors.RemoveAt(0);
                    r_pos[i] = positions[0];
                    positions.RemoveAt(0);
                    i++;
                }
                if (i == num_lights)
                    draw = true;
                if (i > 0 && i < num_lights && lightTextures.Count == 0)
                    fill = true;
            } while (lightTextures.Count > 0 || draw || fill);

            Gorgon.CurrentShader = null;
            if (source != screenShadows)
            {
                Gorgon.CurrentRenderTarget = screenShadows;
                source.Image.Blit(0, 0, source.Width, source.Height, Color.White, BlitterSizeMode.Crop);
            }
            Gorgon.CurrentRenderTarget = null;
        }

        private void RenderPlayerVisionMap()
        {
            Vector2D blitPos;
            if (bFullVision)
            {
                playerOcclusionTarget.Clear(Color.LightGray);
                return;
            }
            if (bPlayerVision)
            {
                playerOcclusionTarget.Clear(Color.Black);
                playerVision.Move(
                    PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position);
                LightArea area = GetLightArea(RadiusToShadowMapSize(playerVision.Radius));
                area.LightPosition = playerVision.Position; // Set the light position

                ITile t = MapManager.GetFloorAt(playerVision.Position);

                if (t != null &&
                    t.Opaque)
                {
                    area.LightPosition = new Vector2D(area.LightPosition.X,
                                                      t.Position.Y +
                                                      MapManager.GetTileSpacing() + 1);
                }


                area.BeginDrawingShadowCasters(); // Start drawing to the light rendertarget
                DrawWallsRelativeToLight(area); // Draw all shadowcasting stuff here in black
                area.EndDrawingShadowCasters(); // End drawing to the light rendertarget

                blitPos = ClientWindowData.WorldToScreen(area.LightPosition - area.LightAreaSize * 0.5f);
                if (debugWallOccluders)
                {
                    RenderTarget previous = Gorgon.CurrentRenderTarget;
                    Gorgon.CurrentRenderTarget = _occluderDebugTarget;
                    _occluderDebugTarget.Clear(Color.White);
                    area.renderTarget.Blit(blitPos.X, blitPos.Y, area.renderTarget.Width, area.renderTarget.Height, Color.White,
                                           BlitterSizeMode.Crop);
                    Gorgon.CurrentRenderTarget = previous;
                }

                shadowMapResolver.ResolveShadows(area.renderTarget.Image, area.renderTarget, area.LightPosition, false,
                                                 IoCManager.Resolve<IResourceManager>().GetSprite("whitemask").Image,
                                                 Vector4D.Zero, new Vector4D(1, 1, 1, 1)); // Calc shadows

                Gorgon.CurrentRenderTarget = playerOcclusionTarget; // Set to shadow rendertarget

                area.renderTarget.SourceBlend = AlphaBlendOperation.One; //Additive blending
                area.renderTarget.DestinationBlend = AlphaBlendOperation.Zero; //Additive blending
                area.renderTarget.Blit(blitPos.X, blitPos.Y, area.renderTarget.Width,
                                       area.renderTarget.Height, Color.White, BlitterSizeMode.Crop);
                // Draw the lights effects
                area.renderTarget.SourceBlend = AlphaBlendOperation.SourceAlpha; //reset blend mode
                area.renderTarget.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha; //reset blend mode
            }
            else
            {
                playerOcclusionTarget.Clear(Color.Black);
            }
        }

        private void CalculateLightArea(ILight l)
        {
            ILightArea area = l.LightArea;
            if (area.Calculated)
                return;
            area.LightPosition = l.Position; //mousePosWorld; // Set the light position
            ITile t = MapManager.GetAllTilesAt(l.Position).FirstOrDefault();
            if (t == null)
                return;
            if (MapManager.GetFloorAt(l.Position).Opaque)
            {
                area.LightPosition = new Vector2D(area.LightPosition.X,
                                                  MapManager.GetAllTilesAt(l.Position).FirstOrDefault().Position.Y +
                                                  MapManager.GetTileSpacing() + 1);
            }
            area.BeginDrawingShadowCasters(); // Start drawing to the light rendertarget
            DrawWallsRelativeToLight(area); // Draw all shadowcasting stuff here in black
            area.EndDrawingShadowCasters(); // End drawing to the light rendertarget
            shadowMapResolver.ResolveShadows(area.renderTarget.Image, area.renderTarget, area.LightPosition, true,
                                             area.Mask.Image, area.MaskProps, Vector4D.Unit); // Calc shadows
            area.Calculated = true;
        }

        private ShadowmapSize RadiusToShadowMapSize(int Radius)
        {
            switch (Radius)
            {
                case 128:
                    return ShadowmapSize.Size128;
                case 256:
                    return ShadowmapSize.Size256;
                case 512:
                    return ShadowmapSize.Size512;
                case 1024:
                    return ShadowmapSize.Size1024;
                default:
                    return ShadowmapSize.Size1024;
            }
        }

        private LightArea GetLightArea(ShadowmapSize size)
        {
            switch (size)
            {
                case ShadowmapSize.Size128:
                    return lightArea128;
                case ShadowmapSize.Size256:
                    return lightArea256;
                case ShadowmapSize.Size512:
                    return lightArea512;
                case ShadowmapSize.Size1024:
                    return lightArea1024;
                default:
                    return lightArea1024;
            }
        }

        // Draws all walls in the area around the light relative to it, and in black (test code, not pretty)
        private void DrawWallsRelativeToLight(ILightArea area)
        {
            RectangleF lightArea = new RectangleF(area.LightPosition - (area.LightAreaSize / 2),
                area.LightAreaSize);

            ITile[] tiles = MapManager.GetAllFloorIn(lightArea);

            foreach (Tile t in tiles)
            {
                if (t.name == "Wall")
                {
                    Vector2D pos = area.ToRelativePosition(t.Position);
                    t.RenderPos(pos.X, pos.Y, MapManager.GetTileSpacing(), (int)area.LightAreaSize.X);
                }
            }
        }

        /// <summary>
        /// Copys all tile sprites into batches.
        /// </summary>
        private void DrawTiles(RectangleF vision)
        {
            int tilespacing = MapManager.GetTileSpacing();

            ITile[] floorTiles = MapManager.GetAllFloorIn(vision);

            foreach (Tile t in floorTiles)
            {
                t.Render(WindowOrigin.X, WindowOrigin.Y, _floorBatch);

                t.RenderGas(WindowOrigin.X, WindowOrigin.Y, tilespacing, _gasBatch);
            }
            /*
            foreach (Tile t in wallTiles.OrderBy(x => x.Position.Y))
            {
                t.Render(WindowOrigin.X, WindowOrigin.Y, _wallBatch);
                t.RenderTop(WindowOrigin.X, WindowOrigin.Y, _wallTopsBatch);
            }//*/
        }

        public void OnTileChanged(PointF tileWorldPosition)
        {
            int ts = IoCManager.Resolve<IMapManager>().GetTileSpacing();
            IoCManager.Resolve<ILightManager>().RecalculateLightsInView(new RectangleF(tileWorldPosition,
                                                                                       new SizeF(ts, ts)));
            // Recalculate the scene batches.
            RecalculateScene();
        }

        #endregion

        #region Nested type: ClickData

        private struct ClickData
        {
            public readonly Entity Clicked;
            public readonly int Drawdepth;

            public ClickData(Entity clicked, int drawdepth)
            {
                Clicked = clicked;
                Drawdepth = drawdepth;
            }
        }

        #endregion
    }
}