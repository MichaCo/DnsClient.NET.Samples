using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DnsClient;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI;
using System.Net;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace UwpApp
{
    public class HostViewModel : INotifyPropertyChanged
    {
        private string queryResult;

        private string logResult;

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public HostViewModel()
        {
            this.QueryResult = "";
            this.LogResult = "initializing..." + Environment.NewLine;
        }

        public string QueryResult
        {
            get { return this.queryResult; }
            set
            {
                this.queryResult = value;
                this.OnPropertyChanged();
            }
        }

        public string LogResult
        {
            get { return this.logResult; }
            set
            {
                this.logResult = value;
                this.OnPropertyChanged();
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private LookupClient _client;

        public HostViewModel ViewModel { get; }

        private ObservableCollection<QueryType> _queryTypes;

        public MainPage()
        {
            this.InitializeComponent();

            _client = new LookupClient(new LookupClientOptions()
            {
                EnableAuditTrail = true,
                ThrowDnsErrors = false,
                Retries = 0,
                Timeout = TimeSpan.FromSeconds(5),
                ContinueOnDnsError = true,
                ContinueOnEmptyResponse = true,
                UseCache = false
            });

            selectQueryType.Loaded += (ev, args) =>
            {
                selectQueryType.SelectedIndex = 0;
            };

            this.ViewModel = new HostViewModel();

            _queryTypes = new ObservableCollection<QueryType>(GetQueryTypes());

            DnsClient.Tracing.Source.Switch.Level = SourceLevels.All;
            DnsClient.Tracing.Source.Listeners.Add(new LogTraceListener(this.logOutput, scrollLog));
        }

        private class LogTraceListener : TraceListener
        {
            private readonly ScrollViewer scrollLog;
            private StringBuilder buffer;

            public LogTraceListener(TextBlock logBlock, ScrollViewer scrollLog)
            {
                LogBlock = logBlock;
                this.scrollLog = scrollLog;
                this.buffer = new StringBuilder();

                _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () =>
                    {
                        while (true)
                        {
                            await Task.Delay(333);

                            string copy;
                            lock (this.buffer)
                            {
                                copy = this.buffer.ToString();
                                this.buffer.Clear();
                            }

                            if (!string.IsNullOrEmpty(copy))
                            {
                                UpdateLogResult(copy);
                            }
                        }
                    });
            }

            public TextBlock LogBlock { get; }

            public override void Write(string message)
            {
                lock (this.buffer)
                {
                    this.buffer.Append(message);
                }
            }

            public override void WriteLine(string message)
            {
                lock (this.buffer)
                {
                    this.buffer.AppendLine(message);
                }
            }

            private void UpdateLogResult(string newText)
            {
                foreach (var line in newText
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.StartsWith("DnsClient ") ? p.Replace("DnsClient ", "") : p))
                {
                    var span = new Span() { };
                    var value = new Run { Text = line };

                    if (line.StartsWith("Verbose"))
                    {
                        value.Foreground = new SolidColorBrush(Colors.LightGray);
                    }
                    else if (line.StartsWith("Information"))
                    {
                        value.Foreground = new SolidColorBrush(Colors.LightSteelBlue);
                    }
                    else if (line.StartsWith("Warning"))
                    {
                        value.Foreground = new SolidColorBrush(Colors.Orange);
                    }
                    else if (line.StartsWith("Error"))
                    {
                        value.Foreground = new SolidColorBrush(Colors.OrangeRed);
                    }

                    span.Inlines.Add(value);
                    span.Inlines.Add(new LineBreak());
                    this.LogBlock.Inlines.Add(span);
                }

                // this.viewModel.LogResult += newText;
                this.scrollLog.UpdateLayout();
                this.scrollLog.ChangeView(0.0f, double.MaxValue, 1.0f);
            }
        }

        private static IEnumerable<QueryType> GetQueryTypes()
        {
            foreach (var type in Enum.GetValues(typeof(QueryType)))
            {
                yield return (QueryType)type;
            }
        }

        private async void TxtQueryKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await PrepQuery();
            }
        }

        private async void QueryClick(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            await PrepQuery();
        }

        private async Task PrepQuery()
        {
            var server = txtServer.Text;

            IPAddress serverIp = null;
            if (server != null)
            {
                try
                {
                    if (!IPAddress.TryParse(server, out serverIp))
                    {
                        var serverResult = await _client.QueryAsync(server, QueryType.A);
                        serverIp = serverResult.Answers.AddressRecords().FirstOrDefault()?.Address;
                    }
                }
                catch { }
            }

            var query = txtQuery.Text;

            var typeString = selectQueryType.SelectedItem == null ? "A" : selectQueryType.SelectedItem.ToString();
            var type = Enum.TryParse<QueryType>(typeString, out var queryType) ? queryType : QueryType.A;
            await RunQuery(query ?? "", type, serverIp);
        }

        private async Task RunQuery(string query, QueryType queryType = QueryType.A, IPAddress server = null)
        {
            this.txtQuery.IsEnabled = false;
            try
            {
                txtOutput.Inlines.Clear();
                var result = server == null ?
                    await _client.QueryAsync(query, queryType) :
                    await _client.QueryServerAsync(new[] { server }, query, queryType);

                foreach (string line in result.AuditTrail.Split(Environment.NewLine))
                {
                    var span = new Span() { };
                    var value = new Run { Text = line };
                    if (line.StartsWith(";"))
                    {
                        value.Foreground = new SolidColorBrush(Colors.LightGray);
                    }
                    if (line.StartsWith(";;"))
                    {
                        value.Foreground = new SolidColorBrush(Colors.DarkGray);
                    }

                    span.Inlines.Add(value);
                    span.Inlines.Add(new LineBreak());
                    txtOutput.Inlines.Add(span);
                }
            }
            catch (Exception ex)
            {
                this.ViewModel.QueryResult = ex.InnerException?.ToString() ?? ex.ToString();
            }
            finally
            {
                this.txtQuery.IsEnabled = true;
            }
        }

        private void AddressesClick(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                var fromFramework = NetworkInterface.GetAllNetworkInterfaces();

                this.ViewModel.QueryResult = $@"
;; NetworkInterface.GetAllNetworkInterfaces
{string.Join("\r\n ", fromFramework.Select(p => p.Name + "\t" + p.NetworkInterfaceType + "\t" + p.GetPhysicalAddress() + "\t" + p.OperationalStatus))}

{string.Join(", ", fromFramework.SelectMany(p => p.GetIPProperties().DnsAddresses))}
;
";
            }
            catch (Exception ex)
            {
                this.ViewModel.QueryResult += $@"
;; NetworkInterface.GetAllNetworkInterfaces failed: {ex.Message}.
;";
            }

            try
            {
                var dnsClientNameservers = NameServer.ResolveNameServers(true, false);
                this.ViewModel.QueryResult += $@"
;; NameServer.ResolveNameServers
{string.Join(", ", dnsClientNameservers)}
;
";
            }
            catch (Exception ex)
            {
                this.ViewModel.QueryResult += $@"
;; NameServer.ResolveNameServers failed: {ex.Message}.
;";
            }

            try
            {
                var interfaces = Interop.IpHlpApi.SystemNetworkInterface.GetAllNetworkInterfaces();

                var info = Interop.IpHlpApi.FixedNetworkInformation.GetFixedNetworkInformation();

                this.ViewModel.QueryResult += $@"
;; Native implementation
{string.Join("\r\n", interfaces.Select(p => p.Name + "\t" + p.NetworkInterfaceType + "\t" + p.GetPhysicalAddress() + "\t" + p.OperationalStatus))}
;;{info.DomainName}
;;{info.HostName}
;;{string.Join(", ", info.DnsAddresses.Select(p => p.ToString()))}
;";
            }
            catch (Exception ex)
            {
                this.ViewModel.QueryResult += $@"
;; Native implementation failed: {ex.Message}.
;";
            }
        }

        private async void SelectQueryTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await PrepQuery();
        }
    }
}