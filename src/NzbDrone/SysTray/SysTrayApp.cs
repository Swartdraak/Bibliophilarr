using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Host;

namespace NzbDrone.SysTray
{
    public class SystemTrayApp : Form, IHostedService
    {
        private readonly IBrowserService _browserService;
        private readonly ILifecycleService _lifecycle;

        private readonly NotifyIcon _trayIcon = new NotifyIcon();
        private readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();

        public SystemTrayApp(IBrowserService browserService, ILifecycleService lifecycle)
        {
            _browserService = browserService;
            _lifecycle = lifecycle;
        }

        public void Start()
        {
            _trayMenu.Items.Add(new ToolStripMenuItem("Launch Browser", null, LaunchBrowser));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

            _trayIcon.Text = string.Format("Bibliophilarr - {0}", BuildInfo.Version);
            _trayIcon.Icon = Properties.Resources.NzbDroneIcon;

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += LaunchBrowser;

            Application.Run(this);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var thread = new Thread(Start);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        // CS0672: In .NET 10, WinForms' `Form.OnClosing` is marked [Obsolete].
        // This form is a tray shell with no user-facing close UI; we still need
        // to dispose the tray icon when the form is destroyed. The `using`
        // directive scopes the suppression and does not weaken the repository's
        // TreatWarningsAsErrors policy for any other file.
#pragma warning disable WFDEV004, CS0672 // Form.OnClosing is obsolete in .NET 10 WinForms; the replacement OnFormClosing will be adopted after the full .NET 10 WinForms surface is available (tracked by #96 NET10-04/#105). This tray shell has no user-facing close UI; OnClosing is preserved for tray-icon disposal only.
        protected override void OnClosing(CancelEventArgs e)
        {
            DisposeTrayIcon();
            base.OnClosing(e);
        }
#pragma warning restore WFDEV004, CS0672

        protected override void OnLoad(EventArgs e)
        {
            Visible = false;
            ShowInTaskbar = false;

            base.OnLoad(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                _trayIcon.Dispose();
            }

            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => Dispose(isDisposing)));
            }
            else
            {
                base.Dispose(isDisposing);
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            _lifecycle.Shutdown();
        }

        private void LaunchBrowser(object sender, EventArgs e)
        {
            try
            {
                _browserService.LaunchWebUI();
            }
            catch (Exception)
            {
            }
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon == null)
            {
                return;
            }

            _trayIcon.Visible = false;
            _trayIcon.Icon = null;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }
}
