using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SFTP_Client
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource _cts;

        public Form1()
        {
            InitializeComponent();
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            if (openKeyFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                txtKeyFile.Text = openKeyFileDialog.FileName;
            }
        }

        private async void btnTest_Click(object sender, EventArgs e)
        {
            var settings = new SftpConnectionSettings
            {
                Host = txtHost.Text,
                Port = (int)numPort.Value,
                Username = txtUsername.Text,
                PrivateKeyPath = txtKeyFile.Text.Trim().Trim('"'),
                PrivateKeyPassphrase = txtPassphrase.Text,
                ExpectedFingerprint = txtFingerprint.Text
            };

            var errors = SftpConnectionTester.Validate(settings);
            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            txtLog.Clear();
            SetBusy(true);
            SetStatus("Testing...", SystemColors.ControlText);
            _cts = new CancellationTokenSource();

            try
            {
                var result = await SftpConnectionTester.TestAsync(settings, Log, _cts.Token);
                Log(result.Message);
                SetStatus(result.Success ? "Success" : "Failed", result.Success ? Color.Green : Color.Firebrick);
            }
            catch (OperationCanceledException)
            {
                Log("Connection test was cancelled.");
                SetStatus("Cancelled", Color.DarkOrange);
            }
            catch (Exception ex)
            {
                Log("Unexpected error: " + ex.Message);
                SetStatus("Failed", Color.Firebrick);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
        }

        private void SetBusy(bool busy)
        {
            btnTest.Enabled = !busy;
            btnBrowse.Enabled = !busy;
            btnCancel.Enabled = busy;
            UseWaitCursor = busy;
        }

        private void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private void Log(string message)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), message);
                return;
            }

            txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine));
        }
    }
}
