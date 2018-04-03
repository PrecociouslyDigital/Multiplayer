﻿using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;

namespace Multiplayer.Client
{
    public class ChatWindow : Window
    {
        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);
            }
        }

        private Vector2 scrollPos;
        private float messagesHeight;
        private List<string> messages = new List<string>();
        private string currentMsg = "";

        public string[] playerList = new string[0];

        public ChatWindow()
        {
            absorbInputAroundWindow = false;
            draggable = true;
            soundClose = null;
            preventCameraMotion = false;
            focusWhenOpened = false;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return)
                {
                    SendMsg();
                    Event.current.Use();
                }
            }

            Rect chat = new Rect(inRect.x, inRect.y, inRect.width - 120f, inRect.height);
            DrawChat(chat);

            Rect info = new Rect(chat);
            info.x = chat.xMax + 10f;
            DrawInfo(info);
        }

        public void DrawInfo(Rect rect)
        {
            GUI.color = new Color(1, 1, 1, 0.8f);

            if (Event.current.type == EventType.repaint)
            {
                StringBuilder builder = new StringBuilder();

                builder.Append(Multiplayer.client != null ? "Connected" : "Not connected").Append("\n");

                if (playerList.Length > 0)
                {
                    builder.Append("\nPlayers (").Append(playerList.Length).Append("):\n");
                    builder.Append(String.Join("\n", playerList));
                }

                Widgets.Label(rect, builder.ToString());
            }
        }

        public void DrawChat(Rect rect)
        {
            GUI.BeginGroup(rect);

            Rect outRect = new Rect(0f, 0f, rect.width, rect.height - 30f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, messagesHeight + 10f);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            float yPos = 0;
            float width = viewRect.width;
            GUI.color = Color.white;

            foreach (string msg in messages)
            {
                float height = Text.CalcHeight(msg, width);

                Widgets.Label(new Rect(20f, yPos, width, height), msg);

                yPos += height;
            }

            if (Event.current.type == EventType.layout)
                messagesHeight = yPos;

            Widgets.EndScrollView();

            Rect textField = new Rect(20f, outRect.yMax + 5f, width - 55f, 25f);
            currentMsg = Widgets.TextField(textField, currentMsg);
            currentMsg = currentMsg.Substring(0, Math.Min(currentMsg.Length, 64));

            if (Widgets.ButtonText(new Rect(textField.xMax + 5f, textField.y, 50f, textField.height), "Send"))
            {
                SendMsg();
            }

            GUI.EndGroup();
        }

        public void SendMsg()
        {
            currentMsg = currentMsg.Trim();

            if (currentMsg.NullOrEmpty()) return;

            if (Multiplayer.client == null)
                AddMsg(Multiplayer.username + ": " + currentMsg);
            else
                Multiplayer.client.Send(Packets.CLIENT_CHAT, currentMsg);

            currentMsg = "";
        }

        public void AddMsg(string msg)
        {
            messages.Add(msg);
            scrollPos.y = messagesHeight;
        }
    }

    public class CustomSelectLandingSite : Page_SelectLandingSite
    {
        public CustomSelectLandingSite()
        {
            this.forcePause = false;
            this.closeOnEscapeKey = false;
        }

        protected override void DoBack()
        {
            Multiplayer.client.Close();

            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = null;
            }, "Entry", "LoadingLongEvent", true, null);
        }
    }

    public class ConnectWindow : Window
    {
        private string ip = "127.0.0.1";

        public override Vector2 InitialSize => new Vector2(300f, 150f);

        public ConnectWindow()
        {
            this.doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(0, 15f, inRect.width, 35f), ip);

            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f), "Connect"))
            {
                int port = MultiplayerServer.DEFAULT_PORT;
                string[] ipport = ip.Split(':');
                if (ipport.Length == 2)
                    int.TryParse(ipport[1], out port);
                else
                    port = MultiplayerServer.DEFAULT_PORT;

                if (!IPAddress.TryParse(ipport[0], out IPAddress address))
                {
                    Messages.Message("Invalid IP address.", MessageTypeDefOf.RejectInput);
                }
                else
                {
                    this.Close(true);
                    Find.WindowStack.Add(new ConnectingWindow(address, port));
                }
            }
        }
    }

    public class ConnectingWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private IPAddress address;
        private int port;

        public string text;

        public ConnectingWindow(IPAddress address, int port)
        {
            this.address = address;
            this.port = port;

            text = "Connecting to " + address.ToString() + ":" + port;

            Client.TryConnect(address, port, conn =>
            {
                text = "Connected.";

                Multiplayer.chat = new ChatWindow();
                Multiplayer.client = conn;

                conn.closedCallback += () =>
                {
                    MpLog.Log("Client connection closed.");
                };

                conn.username = Multiplayer.username;
                conn.State = new ClientWorldState(conn);
            }, exception =>
            {
                text = "Error: Connection failed.";
                Multiplayer.client = null;
            });
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(inRect, text);

            Text.Font = GameFont.Small;
            Rect rect2 = new Rect(inRect.width / 2f - CloseButSize.x / 2f, inRect.height - 55f, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(rect2, "Cancel", true, false, true))
            {
                if (Multiplayer.client != null && Multiplayer.client.IsConnected())
                {
                    Log.Message("Closed client connection");
                    Multiplayer.client.Close();
                }

                Multiplayer.client = null;
                Close();
            }
        }
    }

    public class HostWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(300f, 150f);

        private string ip = "127.0.0.1";

        public override void DoWindowContents(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(0, 15f, inRect.width, 35f), ip);

            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, inRect.height - 35f, 100f, 35f), "Host") && IPAddress.TryParse(ip, out IPAddress ipAddr))
            {
                try
                {
                    MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();

                    Faction.OfPlayer.Name = Multiplayer.username + "'s faction";
                    comp.playerFactions[Multiplayer.username] = Faction.OfPlayer;

                    Multiplayer.localServer = new MultiplayerServer(ipAddr);
                    MultiplayerServer.instance = Multiplayer.localServer;

                    Multiplayer.localServer.highestUniqueId = Find.UniqueIDsManager.GetNextThingID();
                    Multiplayer.localServer.globalBlock = Multiplayer.localServer.NextIdBlock();
                    Multiplayer.globalBlock = Multiplayer.localServer.globalBlock;

                    foreach (Settlement settlement in Find.WorldObjects.Settlements)
                        if (settlement.HasMap)
                            Multiplayer.localServer.mapTiles[settlement.Tile] = settlement.Map.uniqueID;

                    Thread thread = new Thread(Multiplayer.localServer.Run)
                    {
                        Name = "Local server thread"
                    };
                    thread.Start();

                    LocalClientConnection localClient = new LocalClientConnection()
                    {
                        username = Multiplayer.username,
                        onMainThread = OnMainThread.Enqueue
                    };

                    LocalServerConnection localServerConn = new LocalServerConnection()
                    {
                        username = localClient.username,
                        onMainThread = Multiplayer.localServer.Enqueue
                    };

                    localClient.State = new ClientPlayingState(localClient);
                    localServerConn.State = new ServerPlayingState(localServerConn);

                    localServerConn.client = localClient;
                    localClient.server = localServerConn;

                    Multiplayer.localServer.server.GetConnections().Add(localServerConn);

                    Multiplayer.localServer.host = localServerConn;
                    Multiplayer.client = localClient;

                    Multiplayer.chat = new ChatWindow();
                    MultiplayerServer.instance.UpdatePlayerList();

                    Messages.Message("Server started. Listening at " + ipAddr.ToString() + ":" + MultiplayerServer.DEFAULT_PORT, MessageTypeDefOf.SilentInput);
                }
                catch (SocketException)
                {
                    Messages.Message("Server creation failed.", MessageTypeDefOf.RejectInput);
                }

                this.Close(true);
            }
        }
    }

    public class Dialog_JumpTo : Dialog_Rename
    {
        private Action<int> action;

        public Dialog_JumpTo(Action<int> action)
        {
            this.action = action;
        }

        protected override void SetName(string name)
        {
            if (int.TryParse(name, out int tile))
            {
                action(tile);
            }
        }
    }

    public class ThinkTreeWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(UI.screenWidth, 500f);

        const int NodeWidth = 10;

        public class Node
        {
            public string label;
            public int depth;
            public Node parent;
            public List<Node> children = new List<Node>();
            public ThinkNode thinkNode;

            public int width;
            public int y;
            public int x;

            public Node(ThinkNode thinkNode, string label, Node parent = null)
            {
                this.thinkNode = thinkNode;
                this.label = label;
                this.parent = parent;

                if (parent != null)
                {
                    depth = parent.depth + 1;
                    parent.children.Add(this);
                }
            }
        }

        private List<List<Node>> tree = new List<List<Node>>();
        private Pawn pawn;

        public ThinkTreeWindow(Pawn pawn)
        {
            this.doCloseButton = true;
            this.doCloseX = true;
            this.draggable = true;
            this.pawn = pawn;

            ThinkNode thinkRoot = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike").thinkRoot;
            AddNode(thinkRoot, new Node(thinkRoot, "Root"));

            for (int i = tree.Count - 1; i >= 0; i--)
                foreach (Node n in tree[i])
                    if (n.children.Count == 0)
                        n.width = NodeWidth;
                    else
                        n.width = n.children.Sum(c => c.width);

            tree[0][0].x = UI.screenWidth / 2;

            CalcNode(tree[0][0]);
        }

        public void AddNode(ThinkNode from, Node node)
        {
            List<Node> nodes;
            if (tree.Count <= node.depth)
                tree.Add(nodes = new List<Node>());
            else
                nodes = tree[node.depth];
            nodes.Add(node);

            if (from is ThinkNode_Duty) return;

            foreach (ThinkNode child in from.subNodes)
                AddNode(child, new Node(child, child.GetType().Name, node));
        }

        private Vector2 scroll;
        private int y;
        private float scale = 1f;

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            if (Event.current.type == EventType.ScrollWheel)
            {
                scale -= Event.current.delta.y * 0.01f;
                Log.Message("scale " + scale);
            }

            Rect outRect = new Rect(0, 0, inRect.width / scale, (inRect.height - 60) / scale);

            Widgets.BeginScrollView(outRect, ref scroll, new Rect(0, 0, inRect.width, y));

            Vector3 pivot = new Vector3(windowRect.x, windowRect.y);
            Matrix4x4 keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Translate(pivot) * Matrix4x4.Scale(new Vector3(scale, scale, scale)) * Matrix4x4.Translate(-pivot);

            if (Event.current.type == EventType.layout)
                y = 0;

            //NodeLabel(tree[0][0]);

            for (int i = 0; i < tree.Count; i++)
            {
                foreach (Node n in tree[i])
                {
                    if (n.parent != null)
                        Widgets.DrawLine(new Vector2(n.x, n.depth * 20), new Vector2(n.parent.x, n.parent.depth * 20), Color.blue, 2f);

                    Color c = Color.yellow;
                    if (n.thinkNode is ThinkNode_Conditional cond)
                    {
                        c = Color.red;
                        if ((bool)cond_satisfied.Invoke(cond, new object[] { pawn }) == !cond.invert)
                            c = Color.green;
                    }
                    Widgets.DrawRectFast(new Rect(n.x, i * 20, 5, 5), c);
                }
            }

            GUI.matrix = keep;

            Widgets.EndScrollView();
        }

        private static MethodInfo cond_satisfied = typeof(ThinkNode_Conditional).GetMethod("Satisfied", BindingFlags.NonPublic | BindingFlags.Instance);

        public void CalcNode(Node n)
        {
            int x = 0;
            foreach (Node c in n.children)
            {
                c.x = x + n.x - n.width / 2 + c.width / 2;
                x += c.width;
                CalcNode(c);
            }
        }

        public void NodeLabel(Node n)
        {
            if (Event.current.type == EventType.layout)
                n.y = y;

            string text = n.label + " " + n.width;
            if (n.thinkNode is ThinkNode_Conditional cond)
                text += " " + cond_satisfied.Invoke(cond, new object[] { pawn });

            Vector2 size = Text.CalcSize(text);
            Widgets.Label(new Rect(n.depth * 10, n.y, size.x, size.y), text);

            if (Event.current.type == EventType.layout)
                y += 20;

            foreach (Node c in n.children)
                NodeLabel(c);
        }
    }

}
