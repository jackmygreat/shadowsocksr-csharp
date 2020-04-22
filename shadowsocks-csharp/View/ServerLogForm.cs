﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Shadowsocks.Controller;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;

namespace Shadowsocks.View
{
    public partial class ServerLogForm : Form
    {
        private MenuItem clearItem;

        private readonly ShadowsocksController controller;
        private bool firstDispley = true;
        private int lastRefreshIndex;
        private readonly List<int> listOrder = new List<int>();
        private int pendingUpdate;
        private bool rowChange;
        private ServerSpeedLogShow[] ServerSpeedLogList;

        private readonly string title_perfix = "";

        //private ContextMenu contextMenu1;
        private readonly MenuItem topmostItem;
        private int updatePause;
        private int updateSize;
        private int updateTick;
        private readonly AutoResetEvent workerEvent = new AutoResetEvent(false);
        private Thread workerThread;

        public ServerLogForm(ShadowsocksController controller)
        {
            this.controller = controller;
            try
            {
                Icon = Icon.FromHandle(new Bitmap("icon.png").GetHicon());
                title_perfix = Application.StartupPath;
                if (title_perfix.Length > 20)
                    title_perfix = title_perfix.Substring(0, 20);
            }
            catch
            {
                Icon = Icon.FromHandle(Resources.ssw128.GetHicon());
            }

            Font = SystemFonts.MessageBoxFont;
            InitializeComponent();

            Width = 810;
            var dpi_mul = Utils.GetDpiMul();

            var config = controller.GetCurrentConfiguration();
            if (config.configs.Count < 8)
                Height = 300 * dpi_mul / 4;
            else if (config.configs.Count < 20)
                Height = (300 + (config.configs.Count - 8) * 16) * dpi_mul / 4;
            else
                Height = 500 * dpi_mul / 4;
            UpdateTexts();
            UpdateLog();

            Menu = new MainMenu(new[]
            {
                CreateMenuGroup("&Control", new[]
                {
                    CreateMenuItem("&Disconnect direct connections", DisconnectForward_Click),
                    CreateMenuItem("Disconnect &All", Disconnect_Click),
                    new MenuItem("-"),
                    CreateMenuItem("Clear &MaxSpeed", ClearMaxSpeed_Click),
                    clearItem = CreateMenuItem("&Clear", ClearItem_Click),
                    new MenuItem("-"),
                    CreateMenuItem("Clear &Selected Total", ClearSelectedTotal_Click),
                    CreateMenuItem("Clear &Total", ClearTotal_Click)
                }),
                CreateMenuGroup("Port &out", new[]
                {
                    CreateMenuItem("Copy current link", copyLinkItem_Click),
                    CreateMenuItem("Copy current group links", copyGroupLinkItem_Click),
                    CreateMenuItem("Copy all enable links", copyEnableLinksItem_Click),
                    CreateMenuItem("Copy all links", copyLinksItem_Click)
                }),
                CreateMenuGroup("&Window", new[]
                {
                    CreateMenuItem("Auto &size", autosizeItem_Click),
                    topmostItem = CreateMenuItem("Always On &Top", topmostItem_Click)
                })
            });
            controller.ConfigChanged += controller_ConfigChanged;

            for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
                ServerDataGrid.Columns[i].Width = ServerDataGrid.Columns[i].Width * dpi_mul / 4;

            ServerDataGrid.RowTemplate.Height = 20 * dpi_mul / 4;
            //ServerDataGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            var width = 0;
            for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
            {
                if (!ServerDataGrid.Columns[i].Visible)
                    continue;
                width += ServerDataGrid.Columns[i].Width;
            }

            Width = width + SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
            ServerDataGrid.AutoResizeColumnHeadersHeight();
        }

        private MenuItem CreateMenuGroup(string text, MenuItem[] items)
        {
            return new MenuItem(I18N.GetString(text), items);
        }

        private MenuItem CreateMenuItem(string text, EventHandler click)
        {
            return new MenuItem(I18N.GetString(text), click);
        }

        private void UpdateTitle()
        {
            Text = title_perfix + I18N.GetString("ServerLog") + "("
                   + (controller.GetCurrentConfiguration().shareOverLan ? "any" : "local") + ":" +
                   controller.GetCurrentConfiguration().localPort
                   + "(" + Model.Server.GetForwardServerRef().GetConnections().Count + ")"
                   + " " + I18N.GetString("Version") + UpdateChecker.FullVersion
                   + ")";
        }

        private void UpdateTexts()
        {
            UpdateTitle();
            for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
                ServerDataGrid.Columns[i].HeaderText = I18N.GetString(ServerDataGrid.Columns[i].HeaderText);
        }

        private void controller_ConfigChanged(object sender, EventArgs e)
        {
            UpdateTitle();
        }

        private string FormatBytes(long bytes)
        {
            const long K = 1024L;
            const long M = K * 1024L;
            const long G = M * 1024L;
            const long T = G * 1024L;
            const long P = T * 1024L;
            const long E = P * 1024L;

            if (bytes >= M * 990)
            {
                if (bytes >= G * 990)
                {
                    if (bytes >= P * 990)
                        return (bytes / (double) E).ToString("F3") + "E";
                    if (bytes >= T * 990)
                        return (bytes / (double) P).ToString("F3") + "P";
                    return (bytes / (double) T).ToString("F3") + "T";
                }

                if (bytes >= G * 99)
                    return (bytes / (double) G).ToString("F2") + "G";
                if (bytes >= G * 9)
                    return (bytes / (double) G).ToString("F3") + "G";
                return (bytes / (double) G).ToString("F4") + "G";
            }

            if (bytes >= K * 990)
            {
                if (bytes >= M * 100)
                    return (bytes / (double) M).ToString("F1") + "M";
                if (bytes > M * 9.9)
                    return (bytes / (double) M).ToString("F2") + "M";
                return (bytes / (double) M).ToString("F3") + "M";
            }

            if (bytes > K * 99)
                return (bytes / (double) K).ToString("F0") + "K";
            if (bytes > 900)
                return (bytes / (double) K).ToString("F1") + "K";
            return bytes.ToString();
        }

        public bool SetBackColor(DataGridViewCell cell, Color newColor)
        {
            if (cell.Style.BackColor != newColor)
            {
                cell.Style.BackColor = newColor;
                rowChange = true;
                return true;
            }

            return false;
        }

        public bool SetCellToolTipText(DataGridViewCell cell, string newString)
        {
            if (cell.ToolTipText != newString)
            {
                cell.ToolTipText = newString;
                rowChange = true;
                return true;
            }

            return false;
        }

        public bool SetCellText(DataGridViewCell cell, string newString)
        {
            if ((string) cell.Value != newString)
            {
                cell.Value = newString;
                rowChange = true;
                return true;
            }

            return false;
        }

        public bool SetCellText(DataGridViewCell cell, long newInteger)
        {
            if ((string) cell.Value != newInteger.ToString())
            {
                cell.Value = newInteger.ToString();
                rowChange = true;
                return true;
            }

            return false;
        }

        private byte ColorMix(byte a, byte b, double alpha)
        {
            return (byte) (b * alpha + a * (1 - alpha));
        }

        private Color ColorMix(Color a, Color b, double alpha)
        {
            return Color.FromArgb(ColorMix(a.R, b.R, alpha),
                ColorMix(a.G, b.G, alpha),
                ColorMix(a.B, b.B, alpha));
        }

        public void UpdateLogThread()
        {
            while (workerThread != null)
            {
                var config = controller.GetCurrentConfiguration();
                var _ServerSpeedLogList = new ServerSpeedLogShow[config.configs.Count];
                for (var i = 0; i < config.configs.Count && i < _ServerSpeedLogList.Length; ++i)
                    _ServerSpeedLogList[i] = config.configs[i].ServerSpeedLog().Translate();
                ServerSpeedLogList = _ServerSpeedLogList;

                workerEvent.WaitOne();
            }
        }

        public void UpdateLog()
        {
            if (workerThread == null)
            {
                workerThread = new Thread(UpdateLogThread);
                workerThread.Start();
            }
            else
            {
                workerEvent.Set();
            }
        }

        public void RefreshLog()
        {
            if (ServerSpeedLogList == null)
                return;

            var last_rowcount = ServerDataGrid.RowCount;
            var config = controller.GetCurrentConfiguration();
            if (listOrder.Count > config.configs.Count)
                listOrder.RemoveRange(config.configs.Count, listOrder.Count - config.configs.Count);
            while (listOrder.Count < config.configs.Count) listOrder.Add(0);
            while (ServerDataGrid.RowCount < config.configs.Count &&
                   ServerDataGrid.RowCount < ServerSpeedLogList.Length)
            {
                ServerDataGrid.Rows.Add();
                var id = ServerDataGrid.RowCount - 1;
                ServerDataGrid[0, id].Value = id;
            }

            if (ServerDataGrid.RowCount > config.configs.Count)
                for (var list_index = 0; list_index < ServerDataGrid.RowCount; ++list_index)
                {
                    var id_cell = ServerDataGrid[0, list_index];
                    var id = (int) id_cell.Value;
                    if (id >= config.configs.Count)
                    {
                        ServerDataGrid.Rows.RemoveAt(list_index);
                        --list_index;
                    }
                }

            var displayBeginIndex = ServerDataGrid.FirstDisplayedScrollingRowIndex;
            var displayEndIndex = displayBeginIndex + ServerDataGrid.DisplayedRowCount(true);
            try
            {
                for (int list_index = lastRefreshIndex >= ServerDataGrid.RowCount ? 0 : lastRefreshIndex,
                    rowChangeCnt = 0;
                    list_index < ServerDataGrid.RowCount && rowChangeCnt <= 100;
                    ++list_index)
                {
                    lastRefreshIndex = list_index + 1;

                    var id_cell = ServerDataGrid[0, list_index];
                    var id = (int) id_cell.Value;
                    var server = config.configs[id];
                    var serverSpeedLog = ServerSpeedLogList[id];
                    listOrder[id] = list_index;
                    rowChange = false;
                    for (var curcol = 0; curcol < ServerDataGrid.Columns.Count; ++curcol)
                    {
                        if (!firstDispley
                            && (ServerDataGrid.SortedColumn == null || ServerDataGrid.SortedColumn.Index != curcol)
                            && (list_index < displayBeginIndex || list_index >= displayEndIndex))
                            continue;
                        var cell = ServerDataGrid[curcol, list_index];
                        var columnName = ServerDataGrid.Columns[curcol].Name;
                        // Server
                        if (columnName == "Server")
                        {
                            if (config.index == id)
                                SetBackColor(cell, Color.Cyan);
                            else
                                SetBackColor(cell, Color.White);
                            SetCellText(cell, server.FriendlyName());
                        }

                        if (columnName == "Group") SetCellText(cell, server.group);
                        // Enable
                        if (columnName == "Enable")
                        {
                            if (server.isEnable())
                                SetBackColor(cell, Color.White);
                            else
                                SetBackColor(cell, Color.Red);
                        }
                        // TotalConnectTimes
                        else if (columnName == "TotalConnect")
                        {
                            SetCellText(cell, serverSpeedLog.totalConnectTimes);
                        }
                        // TotalConnecting
                        else if (columnName == "Connecting")
                        {
                            var connections = serverSpeedLog.totalConnectTimes - serverSpeedLog.totalDisconnectTimes;
                            //long ref_connections = server.GetConnections().Count;
                            //if (ref_connections < connections)
                            //{
                            //    connections = ref_connections;
                            //}
                            var colList = new Color[5]
                                {Color.White, Color.LightGreen, Color.Yellow, Color.Red, Color.Red};
                            var bytesList = new long[5] {0, 16, 32, 64, 65536};
                            for (var i = 1; i < colList.Length; ++i)
                                if (connections < bytesList[i])
                                {
                                    SetBackColor(cell,
                                        ColorMix(colList[i - 1],
                                            colList[i],
                                            (double) (connections - bytesList[i - 1]) /
                                            (bytesList[i] - bytesList[i - 1])
                                        )
                                    );
                                    break;
                                }

                            SetCellText(cell, serverSpeedLog.totalConnectTimes - serverSpeedLog.totalDisconnectTimes);
                        }
                        // AvgConnectTime
                        else if (columnName == "AvgLatency")
                        {
                            if (serverSpeedLog.avgConnectTime >= 0)
                                SetCellText(cell, serverSpeedLog.avgConnectTime / 1000);
                            else
                                SetCellText(cell, "-");
                        }
                        // AvgDownSpeed
                        else if (columnName == "AvgDownSpeed")
                        {
                            var avgBytes = serverSpeedLog.avgDownloadBytes;
                            var valStr = FormatBytes(avgBytes);
                            var colList = new Color[6]
                                {Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red};
                            var bytesList = new long[6]
                            {
                                0, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024L * 1024 * 1024 * 1024
                            };
                            for (var i = 1; i < colList.Length; ++i)
                                if (avgBytes < bytesList[i])
                                {
                                    SetBackColor(cell,
                                        ColorMix(colList[i - 1],
                                            colList[i],
                                            (double) (avgBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])
                                        )
                                    );
                                    break;
                                }

                            SetCellText(cell, valStr);
                        }
                        // MaxDownSpeed
                        else if (columnName == "MaxDownSpeed")
                        {
                            var maxBytes = serverSpeedLog.maxDownloadBytes;
                            var valStr = FormatBytes(maxBytes);
                            var colList = new Color[6]
                                {Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red};
                            var bytesList = new long[6]
                                {0, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024 * 1024 * 1024};
                            for (var i = 1; i < colList.Length; ++i)
                                if (maxBytes < bytesList[i])
                                {
                                    SetBackColor(cell,
                                        ColorMix(colList[i - 1],
                                            colList[i],
                                            (double) (maxBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])
                                        )
                                    );
                                    break;
                                }

                            SetCellText(cell, valStr);
                        }
                        // AvgUpSpeed
                        else if (columnName == "AvgUpSpeed")
                        {
                            var avgBytes = serverSpeedLog.avgUploadBytes;
                            var valStr = FormatBytes(avgBytes);
                            var colList = new Color[6]
                                {Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red};
                            var bytesList = new long[6]
                            {
                                0, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024L * 1024 * 1024 * 1024
                            };
                            for (var i = 1; i < colList.Length; ++i)
                                if (avgBytes < bytesList[i])
                                {
                                    SetBackColor(cell,
                                        ColorMix(colList[i - 1],
                                            colList[i],
                                            (double) (avgBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])
                                        )
                                    );
                                    break;
                                }

                            SetCellText(cell, valStr);
                        }
                        // MaxUpSpeed
                        else if (columnName == "MaxUpSpeed")
                        {
                            var maxBytes = serverSpeedLog.maxUploadBytes;
                            var valStr = FormatBytes(maxBytes);
                            var colList = new Color[6]
                                {Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red};
                            var bytesList = new long[6]
                                {0, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024 * 1024 * 1024};
                            for (var i = 1; i < colList.Length; ++i)
                                if (maxBytes < bytesList[i])
                                {
                                    SetBackColor(cell,
                                        ColorMix(colList[i - 1],
                                            colList[i],
                                            (double) (maxBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])
                                        )
                                    );
                                    break;
                                }

                            SetCellText(cell, valStr);
                        }
                        // TotalUploadBytes
                        else if (columnName == "Upload")
                        {
                            var valStr = FormatBytes(serverSpeedLog.totalUploadBytes);
                            var fullVal = serverSpeedLog.totalUploadBytes.ToString();
                            if (cell.ToolTipText != fullVal)
                            {
                                if (fullVal == "0")
                                {
                                    SetBackColor(cell, Color.FromArgb(0xf4, 0xff, 0xf4));
                                }
                                else
                                {
                                    SetBackColor(cell, Color.LightGreen);
                                    cell.Tag = 8;
                                }
                            }
                            else if (cell.Tag != null)
                            {
                                cell.Tag = (int) cell.Tag - 1;
                                if ((int) cell.Tag == 0) SetBackColor(cell, Color.FromArgb(0xf4, 0xff, 0xf4));
                                //Color col = cell.Style.BackColor;
                                //SetBackColor(cell, Color.FromArgb(Math.Min(255, col.R + colAdd), Math.Min(255, col.G + colAdd), Math.Min(255, col.B + colAdd)));
                            }

                            SetCellToolTipText(cell, fullVal);
                            SetCellText(cell, valStr);
                        }
                        // TotalDownloadBytes
                        else if (columnName == "Download")
                        {
                            var valStr = FormatBytes(serverSpeedLog.totalDownloadBytes);
                            var fullVal = serverSpeedLog.totalDownloadBytes.ToString();
                            if (cell.ToolTipText != fullVal)
                            {
                                if (fullVal == "0")
                                {
                                    SetBackColor(cell, Color.FromArgb(0xff, 0xf0, 0xf0));
                                }
                                else
                                {
                                    SetBackColor(cell, Color.LightGreen);
                                    cell.Tag = 8;
                                }
                            }
                            else if (cell.Tag != null)
                            {
                                cell.Tag = (int) cell.Tag - 1;
                                if ((int) cell.Tag == 0) SetBackColor(cell, Color.FromArgb(0xff, 0xf0, 0xf0));
                                //Color col = cell.Style.BackColor;
                                //SetBackColor(cell, Color.FromArgb(Math.Min(255, col.R + colAdd), Math.Min(255, col.G + colAdd), Math.Min(255, col.B + colAdd)));
                            }

                            SetCellToolTipText(cell, fullVal);
                            SetCellText(cell, valStr);
                        }
                        else if (columnName == "DownloadRaw")
                        {
                            var valStr = FormatBytes(serverSpeedLog.totalDownloadRawBytes);
                            var fullVal = serverSpeedLog.totalDownloadRawBytes.ToString();
                            if (cell.ToolTipText != fullVal)
                            {
                                if (fullVal == "0")
                                {
                                    SetBackColor(cell, Color.FromArgb(0xff, 0x80, 0x80));
                                }
                                else
                                {
                                    SetBackColor(cell, Color.LightGreen);
                                    cell.Tag = 8;
                                }
                            }
                            else if (cell.Tag != null)
                            {
                                cell.Tag = (int) cell.Tag - 1;
                                if ((int) cell.Tag == 0)
                                {
                                    if (fullVal == "0")
                                        SetBackColor(cell, Color.FromArgb(0xff, 0x80, 0x80));
                                    else
                                        SetBackColor(cell, Color.FromArgb(0xf0, 0xf0, 0xff));
                                }

                                //Color col = cell.Style.BackColor;
                                //SetBackColor(cell, Color.FromArgb(Math.Min(255, col.R + colAdd), Math.Min(255, col.G + colAdd), Math.Min(255, col.B + colAdd)));
                            }

                            SetCellToolTipText(cell, fullVal);
                            SetCellText(cell, valStr);
                        }
                        // ErrorConnectTimes
                        else if (columnName == "ConnectError")
                        {
                            var val = serverSpeedLog.errorConnectTimes + serverSpeedLog.errorDecodeTimes;
                            var col = Color.FromArgb(255, (byte) Math.Max(0, 255 - val * 2.5),
                                (byte) Math.Max(0, 255 - val * 2.5));
                            SetBackColor(cell, col);
                            SetCellText(cell, val);
                        }
                        // ErrorTimeoutTimes
                        else if (columnName == "ConnectTimeout")
                        {
                            SetCellText(cell, serverSpeedLog.errorTimeoutTimes);
                        }
                        // ErrorTimeoutTimes
                        else if (columnName == "ConnectEmpty")
                        {
                            var val = serverSpeedLog.errorEmptyTimes;
                            var col = Color.FromArgb(255, (byte) Math.Max(0, 255 - val * 8),
                                (byte) Math.Max(0, 255 - val * 8));
                            SetBackColor(cell, col);
                            SetCellText(cell, val);
                        }
                        // ErrorContinurousTimes
                        else if (columnName == "Continuous")
                        {
                            var val = serverSpeedLog.errorContinurousTimes;
                            var col = Color.FromArgb(255, (byte) Math.Max(0, 255 - val * 8),
                                (byte) Math.Max(0, 255 - val * 8));
                            SetBackColor(cell, col);
                            SetCellText(cell, val);
                        }
                        // ErrorPersent
                        else if (columnName == "ErrorPercent")
                        {
                            if (serverSpeedLog.errorLogTimes + serverSpeedLog.totalConnectTimes -
                                serverSpeedLog.totalDisconnectTimes > 0)
                            {
                                var percent = (serverSpeedLog.errorConnectTimes
                                               + serverSpeedLog.errorTimeoutTimes
                                               + serverSpeedLog.errorDecodeTimes)
                                              * 100.00
                                              / (serverSpeedLog.errorLogTimes + serverSpeedLog.totalConnectTimes -
                                                 serverSpeedLog.totalDisconnectTimes);
                                SetBackColor(cell,
                                    Color.FromArgb(255, (byte) (255 - percent * 2), (byte) (255 - percent * 2)));
                                SetCellText(cell, percent.ToString("F0") + "%");
                            }
                            else
                            {
                                SetBackColor(cell, Color.White);
                                SetCellText(cell, "-");
                            }
                        }
                    }

                    if (rowChange && list_index >= displayBeginIndex && list_index < displayEndIndex)
                        rowChangeCnt++;
                }
            }
            catch
            {
            }

            UpdateTitle();
            if (ServerDataGrid.SortedColumn != null)
                ServerDataGrid.Sort(ServerDataGrid.SortedColumn,
                    (ListSortDirection) ((int) ServerDataGrid.SortOrder - 1));
            if (last_rowcount == 0 && config.index >= 0 && config.index < ServerDataGrid.RowCount)
                ServerDataGrid[0, config.index].Selected = true;
            if (firstDispley)
            {
                ServerDataGrid.FirstDisplayedScrollingRowIndex =
                    Math.Max(0, config.index - ServerDataGrid.DisplayedRowCount(true) / 2);
                firstDispley = false;
            }
        }

        private void autosizeColumns()
        {
            for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
            {
                var name = ServerDataGrid.Columns[i].Name;
                if (name == "AvgLatency"
                    || name == "AvgDownSpeed"
                    || name == "MaxDownSpeed"
                    || name == "AvgUpSpeed"
                    || name == "MaxUpSpeed"
                    || name == "Upload"
                    || name == "Download"
                    || name == "DownloadRaw"
                    || name == "Group"
                    || name == "Connecting"
                    || name == "ErrorPercent"
                    || name == "ConnectError"
                    || name == "ConnectTimeout"
                    || name == "Continuous"
                    || name == "ConnectEmpty"
                )
                {
                    if (ServerDataGrid.Columns[i].Width <= 2)
                        continue;
                    ServerDataGrid.AutoResizeColumn(i, DataGridViewAutoSizeColumnMode.AllCellsExceptHeader);
                    if (name == "AvgLatency"
                        || name == "Connecting"
                        || name == "AvgDownSpeed"
                        || name == "MaxDownSpeed"
                        || name == "AvgUpSpeed"
                        || name == "MaxUpSpeed"
                    )
                        ServerDataGrid.Columns[i].MinimumWidth = ServerDataGrid.Columns[i].Width;
                }
            }

            var width = 0;
            for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
            {
                if (!ServerDataGrid.Columns[i].Visible)
                    continue;
                width += ServerDataGrid.Columns[i].Width;
            }

            Width = width + SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
            ServerDataGrid.AutoResizeColumnHeadersHeight();
        }

        private void autosizeItem_Click(object sender, EventArgs e)
        {
            autosizeColumns();
        }

        private void copyLinkItem_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            if (config.index >= 0 && config.index < config.configs.Count)
                try
                {
                    var link = config.configs[config.index].GetSSRLinkForServer();
                    Clipboard.SetText(link);
                }
                catch
                {
                }
        }

        private void copyGroupLinkItem_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            if (config.index >= 0 && config.index < config.configs.Count)
            {
                var group = config.configs[config.index].group;
                var link = "";
                for (var index = 0; index < config.configs.Count; ++index)
                {
                    if (config.configs[index].group != group)
                        continue;
                    link += config.configs[index].GetSSRLinkForServer() + "\r\n";
                }

                try
                {
                    Clipboard.SetText(link);
                }
                catch
                {
                }
            }
        }

        private void copyEnableLinksItem_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            var link = "";
            for (var index = 0; index < config.configs.Count; ++index)
            {
                if (!config.configs[index].enable)
                    continue;
                link += config.configs[index].GetSSRLinkForServer() + "\r\n";
            }

            try
            {
                Clipboard.SetText(link);
            }
            catch
            {
            }
        }

        private void copyLinksItem_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            var link = "";
            for (var index = 0; index < config.configs.Count; ++index)
                link += config.configs[index].GetSSRLinkForServer() + "\r\n";
            try
            {
                Clipboard.SetText(link);
            }
            catch
            {
            }
        }

        private void topmostItem_Click(object sender, EventArgs e)
        {
            topmostItem.Checked = !topmostItem.Checked;
            TopMost = topmostItem.Checked;
        }

        private void DisconnectForward_Click(object sender, EventArgs e)
        {
            Model.Server.GetForwardServerRef().GetConnections().CloseAll();
        }

        private void Disconnect_Click(object sender, EventArgs e)
        {
            controller.DisconnectAllConnections();
            Model.Server.GetForwardServerRef().GetConnections().CloseAll();
        }

        private void ClearMaxSpeed_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            foreach (var server in config.configs) server.ServerSpeedLog().ClearMaxSpeed();
        }

        private void ClearSelectedTotal_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            if (config.index >= 0 && config.index < config.configs.Count)
                try
                {
                    controller.ClearTransferTotal(config.configs[config.index].server);
                }
                catch
                {
                }
        }

        private void ClearTotal_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            foreach (var server in config.configs) controller.ClearTransferTotal(server.server);
        }

        private void ClearItem_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            foreach (var server in config.configs) server.ServerSpeedLog().Clear();
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            if (updatePause > 0)
            {
                updatePause -= 1;
                return;
            }

            if (WindowState == FormWindowState.Minimized)
            {
                if (++pendingUpdate < 40) return;
            }
            else
            {
                ++updateTick;
            }

            pendingUpdate = 0;
            RefreshLog();
            UpdateLog();
            if (updateSize > 1) --updateSize;
            if (updateTick == 2 || updateSize == 1)
                updateSize = 0;
            //autosizeColumns();
        }

        private void ServerDataGrid_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
            }
            else if (e.Button == MouseButtons.Left)
            {
                int row_index = -1, col_index = -1;
                if (ServerDataGrid.SelectedCells.Count > 0)
                {
                    row_index = ServerDataGrid.SelectedCells[0].RowIndex;
                    col_index = ServerDataGrid.SelectedCells[0].ColumnIndex;
                }

                if (row_index >= 0)
                {
                    var id = (int) ServerDataGrid[0, row_index].Value;
                    if (ServerDataGrid.Columns[col_index].Name == "Server")
                    {
                        var config = controller.GetCurrentConfiguration();
                        controller.SelectServerIndex(id);
                    }

                    if (ServerDataGrid.Columns[col_index].Name == "Group")
                    {
                        var config = controller.GetCurrentConfiguration();
                        var cur_server = config.configs[id];
                        var group = cur_server.group;
                        if (!string.IsNullOrEmpty(group))
                        {
                            var enable = !cur_server.enable;
                            foreach (var server in config.configs)
                                if (server.group == group)
                                    if (server.enable != enable)
                                        server.setEnable(enable);
                            controller.SelectServerIndex(config.index);
                        }
                    }

                    if (ServerDataGrid.Columns[col_index].Name == "Enable")
                    {
                        var config = controller.GetCurrentConfiguration();
                        var server = config.configs[id];
                        server.setEnable(!server.isEnable());
                        controller.SelectServerIndex(config.index);
                    }

                    ServerDataGrid[0, row_index].Selected = true;
                }
            }
        }

        private void ServerDataGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                var id = (int) ServerDataGrid[0, e.RowIndex].Value;
                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Server")
                {
                    var config = controller.GetCurrentConfiguration();
                    Console.WriteLine("config.checkSwitchAutoCloseAll:" + config.checkSwitchAutoCloseAll);
                    if (config.checkSwitchAutoCloseAll) controller.DisconnectAllConnections();
                    controller.SelectServerIndex(id);
                }

                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Group")
                {
                    var config = controller.GetCurrentConfiguration();
                    var cur_server = config.configs[id];
                    var group = cur_server.group;
                    if (!string.IsNullOrEmpty(group))
                    {
                        var enable = !cur_server.enable;
                        foreach (var server in config.configs)
                            if (server.group == group)
                                if (server.enable != enable)
                                    server.setEnable(enable);
                        controller.SelectServerIndex(config.index);
                    }
                }

                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Enable")
                {
                    var config = controller.GetCurrentConfiguration();
                    var server = config.configs[id];
                    server.setEnable(!server.isEnable());
                    controller.SelectServerIndex(config.index);
                }

                ServerDataGrid[0, e.RowIndex].Selected = true;
            }
        }

        private void ServerDataGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                var id = (int) ServerDataGrid[0, e.RowIndex].Value;
                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "ID") controller.ShowConfigForm(id);
                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Server") controller.ShowConfigForm(id);
                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Connecting")
                {
                    var config = controller.GetCurrentConfiguration();
                    var server = config.configs[id];
                    server.GetConnections().CloseAll();
                }

                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "MaxDownSpeed" ||
                    ServerDataGrid.Columns[e.ColumnIndex].Name == "MaxUpSpeed")
                {
                    var config = controller.GetCurrentConfiguration();
                    config.configs[id].ServerSpeedLog().ClearMaxSpeed();
                }

                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Upload" ||
                    ServerDataGrid.Columns[e.ColumnIndex].Name == "Download")
                {
                    var config = controller.GetCurrentConfiguration();
                    config.configs[id].ServerSpeedLog().ClearTrans();
                }

                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "DownloadRaw")
                {
                    var config = controller.GetCurrentConfiguration();
                    config.configs[id].ServerSpeedLog().Clear();
                    config.configs[id].setEnable(true);
                }

                if (ServerDataGrid.Columns[e.ColumnIndex].Name == "ConnectError"
                    || ServerDataGrid.Columns[e.ColumnIndex].Name == "ConnectTimeout"
                    || ServerDataGrid.Columns[e.ColumnIndex].Name == "ConnectEmpty"
                    || ServerDataGrid.Columns[e.ColumnIndex].Name == "Continuous"
                )
                {
                    var config = controller.GetCurrentConfiguration();
                    config.configs[id].ServerSpeedLog().ClearError();
                    config.configs[id].setEnable(true);
                }

                ServerDataGrid[0, e.RowIndex].Selected = true;
            }
        }

        private void ServerLogForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            controller.ConfigChanged -= controller_ConfigChanged;
            var thread = workerThread;
            workerThread = null;
            while (thread.IsAlive)
            {
                workerEvent.Set();
                Thread.Sleep(50);
            }
        }

        private long Str2Long(string str)
        {
            if (str == "-") return -1;
            //if (String.IsNullOrEmpty(str)) return -1;
            if (str.LastIndexOf('K') > 0)
            {
                var ret = Convert.ToDouble(str.Substring(0, str.LastIndexOf('K')));
                return (long) (ret * 1024);
            }

            if (str.LastIndexOf('M') > 0)
            {
                var ret = Convert.ToDouble(str.Substring(0, str.LastIndexOf('M')));
                return (long) (ret * 1024 * 1024);
            }

            if (str.LastIndexOf('G') > 0)
            {
                var ret = Convert.ToDouble(str.Substring(0, str.LastIndexOf('G')));
                return (long) (ret * 1024 * 1024 * 1024);
            }

            if (str.LastIndexOf('T') > 0)
            {
                var ret = Convert.ToDouble(str.Substring(0, str.LastIndexOf('T')));
                return (long) (ret * 1024 * 1024 * 1024 * 1024);
            }

            try
            {
                var ret = Convert.ToDouble(str);
                return (long) ret;
            }
            catch
            {
                return -1;
            }
        }

        private void ServerDataGrid_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            //e.SortResult = 0;
            if (e.Column.Name == "Server" || e.Column.Name == "Group")
            {
                e.SortResult = string.Compare(Convert.ToString(e.CellValue1), Convert.ToString(e.CellValue2));
                e.Handled = true;
            }
            else if (e.Column.Name == "ID"
                     || e.Column.Name == "TotalConnect"
                     || e.Column.Name == "Connecting"
                     || e.Column.Name == "ConnectError"
                     || e.Column.Name == "ConnectTimeout"
                     || e.Column.Name == "Continuous"
            )
            {
                var v1 = Convert.ToInt32(e.CellValue1);
                var v2 = Convert.ToInt32(e.CellValue2);
                e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
            }
            else if (e.Column.Name == "ErrorPercent")
            {
                var s1 = Convert.ToString(e.CellValue1);
                var s2 = Convert.ToString(e.CellValue2);
                var v1 = s1.Length <= 1 ? 0 : Convert.ToInt32(Convert.ToDouble(s1.Substring(0, s1.Length - 1)) * 100);
                var v2 = s2.Length <= 1 ? 0 : Convert.ToInt32(Convert.ToDouble(s2.Substring(0, s2.Length - 1)) * 100);
                e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
            }
            else if (e.Column.Name == "AvgLatency"
                     || e.Column.Name == "AvgDownSpeed"
                     || e.Column.Name == "MaxDownSpeed"
                     || e.Column.Name == "AvgUpSpeed"
                     || e.Column.Name == "MaxUpSpeed"
                     || e.Column.Name == "Upload"
                     || e.Column.Name == "Download"
                     || e.Column.Name == "DownloadRaw"
            )
            {
                var s1 = Convert.ToString(e.CellValue1);
                var s2 = Convert.ToString(e.CellValue2);
                var v1 = Str2Long(s1);
                var v2 = Str2Long(s2);
                e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
            }

            if (e.SortResult == 0)
            {
                var v1 = listOrder[Convert.ToInt32(ServerDataGrid[0, e.RowIndex1].Value)];
                var v2 = listOrder[Convert.ToInt32(ServerDataGrid[0, e.RowIndex2].Value)];
                e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
                if (e.SortResult != 0 && ServerDataGrid.SortOrder == SortOrder.Descending) e.SortResult = -e.SortResult;
            }

            if (e.SortResult != 0) e.Handled = true;
        }

        private void ServerLogForm_Move(object sender, EventArgs e)
        {
            updatePause = 0;
        }

        protected override void WndProc(ref Message message)
        {
            const int WM_SIZING = 532;
            //const int WM_SIZE = 533;
            const int WM_MOVING = 534;
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MINIMIZE = 0xF020;
            switch (message.Msg)
            {
                case WM_SIZING:
                case WM_MOVING:
                    updatePause = 2;
                    break;
                case WM_SYSCOMMAND:
                    if ((int) message.WParam == SC_MINIMIZE) Utils.ReleaseMemory();
                    break;
            }

            base.WndProc(ref message);
        }

        private void ServerLogForm_ResizeEnd(object sender, EventArgs e)
        {
            updatePause = 0;

            var width = 0;
            for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
            {
                if (!ServerDataGrid.Columns[i].Visible)
                    continue;
                width += ServerDataGrid.Columns[i].Width;
            }

            width += SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
            ServerDataGrid.Columns[2].Width += Width - width;
        }

        private void ServerDataGrid_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e)
        {
            var width = 0;
            for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
            {
                if (!ServerDataGrid.Columns[i].Visible)
                    continue;
                width += ServerDataGrid.Columns[i].Width;
            }

            Width = width + SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
            ServerDataGrid.AutoResizeColumnHeadersHeight();
        }

        private class DoubleBufferListView : DataGridView
        {
            public DoubleBufferListView()
            {
                SetStyle(ControlStyles.DoubleBuffer
                         | ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.UserPaint
                         | ControlStyles.AllPaintingInWmPaint
                    , true);
                UpdateStyles();
            }
        }
    }
}