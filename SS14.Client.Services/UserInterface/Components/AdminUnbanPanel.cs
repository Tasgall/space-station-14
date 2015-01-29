﻿using GorgonLibrary;
using GorgonLibrary.InputDevices;
using Lidgren.Network;
using SS14.Client.Interfaces.Network;
using SS14.Client.Interfaces.Resource;
using SS14.Shared;
using System.Drawing;

namespace SS14.Client.Services.UserInterface.Components
{
    internal class AdminUnbanPanel : Window
    {
        private readonly INetworkManager _networkManager;
        new private readonly IResourceManager _resourceManager;

        public AdminUnbanPanel(Size size, Banlist banlist, INetworkManager networkManager,
                               IResourceManager resourceManager)
            : base("Admin UnBan Panel", size, resourceManager)
        {
            _networkManager = networkManager;
            _resourceManager = resourceManager;

            BuildList(banlist);

            var closeButton = new Button("Close", _resourceManager) {Position = new Point(5, 5)};
            closeButton.Clicked += CloseButtonClicked;
            components.Add(closeButton);

            Position = new Point((int) (Gorgon.CurrentRenderTarget.Width/2f) - (int) (ClientArea.Width/2f),
                                 (int) (Gorgon.CurrentRenderTarget.Height/2f) - (int) (ClientArea.Height/2f));
        }

        private void CloseButtonClicked(Button sender)
        {
            Dispose();
        }

        private void BuildList(Banlist banList)
        {
            int yOffset = 40;
            foreach (BanEntry entry in banList.List)
            {
                var line = new Label("IP: " + entry.ip + "\tReason: " + entry.reason +
                                     "\tTemporary: " + entry.tempBan + "\tExpires: " +
                                     entry.expiresAt.ToString("d/M/yyyy HH:mm:ss"), "CALIBRI", _resourceManager)
                               {Position = new Point(5, yOffset + 5)};

                components.Add(line);
                var unbanButton = new Button("Unban", _resourceManager)
                                      {
                                          Position =
                                              new Point(line.ClientArea.Right + 10,
                                                        yOffset + (int) (line.ClientArea.Height/3f))
                                      };

                components.Add(unbanButton);
                unbanButton.UserData = entry.ip;
                unbanButton.Clicked += UnbanButtClicked;
                unbanButton.Update(0);

                yOffset += 35;
            }
        }

        private void UnbanButtClicked(Button sender)
        {
            NetOutgoingMessage msg = _networkManager.CreateMessage();
            msg.Write((byte) NetMessage.RequestAdminUnBan);
            msg.Write((string) sender.UserData);
            _networkManager.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
            Dispose();
        }

        public override void Update(float frameTime)
        {
            if (disposing || !IsVisible()) return;
            base.Update(frameTime);
        }

        public override void Render()
        {
            if (disposing || !IsVisible()) return;
            base.Render();
        }

        public override void Dispose()
        {
            if (disposing) return;
            base.Dispose();
        }

        public override bool MouseDown(MouseInputEventArgs e)
        {
            if (disposing || !IsVisible()) return false;
            return base.MouseDown(e);
        }

        public override bool MouseUp(MouseInputEventArgs e)
        {
            if (disposing || !IsVisible()) return false;
            return base.MouseUp(e);
        }

        public override void MouseMove(MouseInputEventArgs e)
        {
            if (disposing || !IsVisible()) return;
            base.MouseMove(e);
        }
    }
}