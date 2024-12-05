using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using HtmlAgilityPack;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;

namespace RuntimeMaster
{
    public class GünlükKaydı
    {
        private readonly string _logDizini;
        private static readonly object _kilit = new object();

        public GünlükKaydı(string logPath)
        {
            _logDizini = Path.Combine(logPath, "RuntimeMaster_Günlükler");
            Directory.CreateDirectory(_logDizini);
        }

        public void Bilgi(string mesaj)
        {
            GünlükYaz("BİLGİ", mesaj);
        }

        public void Hata(string mesaj, Exception istisna = null)
        {
            string hataMesajı = istisna != null ?
                $"{mesaj}\nİstisna Türü: {istisna.GetType()}\nHata Mesajı: {istisna.Message}\nHata Detayı: {istisna.StackTrace}" :
                mesaj;

            GünlükYaz("HATA", hataMesajı);
        }

        public void Hata(Exception istisna, string mesaj)
        {
            string hataMesajı = $"{mesaj}\nİstisna Türü: {istisna.GetType()}\nHata Mesajı: {istisna.Message}\nHata Detayı: {istisna.StackTrace}";
            GünlükYaz("HATA", hataMesajı);
        }

        public void Uyarı(string mesaj)
        {
            GünlükYaz("UYARI", mesaj);
        }

        private void GünlükYaz(string seviye, string mesaj)
        {
            string günlükDosyası = Path.Combine(_logDizini, $"RuntimeMaster_Günlük_{DateTime.Now:yyyy-MM-dd}.log");
            string günlükMesajı = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{seviye}] {mesaj}";

            lock (_kilit)
            {
                try
                {
                    File.AppendAllText(günlükDosyası, günlükMesajı + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Günlük kaydı başarısız: {ex.Message}");
                }
            }
        }
    }

    public class DownloadHelper
    {
        private readonly HttpClient _httpClient;
        private readonly GünlükKaydı _günlük;
        private readonly int _maxRedirects;
        private bool _disposed = false;

        public DownloadHelper(GünlükKaydı günlük, int maxRedirects = 10)
        {
            _günlük = günlük;
            _maxRedirects = maxRedirects;

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            ConfigureHttpClient();
        }

        private void ConfigureHttpClient()
        {
            string osVersion = Environment.OSVersion.ToString();
            string architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            string userAgent = $"RuntimeMaster/1.0 ({osVersion}; {architecture}; .NET {Environment.Version})";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/octet-stream,application/x-msdownload,application/exe,application/x-exe,application/msi");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
            _httpClient.DefaultRequestHeaders.Add("DNT", "1");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

            _günlük.Bilgi($"HTTP İstemcisi yapılandırıldı: {userAgent}");
        }

        public async Task<(string Url, string FileName)> ResolveDownloadUrl(string initialUrl)
        {
            string currentUrl = initialUrl;
            int redirectCount = 0;
            string fileName = string.Empty;

            while (redirectCount < _maxRedirects)
            {
                try
                {
                    _günlük.Bilgi($"URL kontrol ediliyor: {currentUrl}");
                    var response = await _httpClient.GetAsync(currentUrl, HttpCompletionOption.ResponseHeadersRead);

                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 399)
                    {
                        var redirectUrl = response.Headers.Location?.ToString();
                        if (string.IsNullOrEmpty(redirectUrl))
                        {
                            throw new Exception("Yönlendirme URL'si boş");
                        }

                        if (!redirectUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseUri = new Uri(currentUrl);
                            redirectUrl = new Uri(baseUri, redirectUrl).ToString();
                        }

                        _günlük.Bilgi($"Yönlendirme algılandı: {redirectUrl}");
                        currentUrl = redirectUrl;
                        redirectCount++;
                        continue;
                    }

                    if (response.Content.Headers.ContentDisposition != null)
                    {
                        fileName = response.Content.Headers.ContentDisposition.FileName?.Trim('"');
                        _günlük.Bilgi($"Content-Disposition'dan dosya adı alındı: {fileName}");
                    }

                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = GetSafeFileName(currentUrl);
                        _günlük.Bilgi($"URL'den dosya adı oluşturuldu: {fileName}");
                    }

                    return (currentUrl, fileName);
                }
                catch (Exception ex)
                {
                    _günlük.Hata($"URL çözümleme hatası: {ex.Message}");
                    throw;
                }
            }

            throw new Exception($"Maksimum yönlendirme sayısı aşıldı ({_maxRedirects})");
        }
        
        private string GetSafeFileName(string url)
        {
            try
            {
                string fileName = Path.GetFileName(new Uri(url).LocalPath);

                if (fileName.Contains("?"))
                {
                    fileName = fileName.Substring(0, fileName.IndexOf("?"));
                }

                foreach (var invalidChar in Path.GetInvalidFileNameChars())
                {
                    fileName = fileName.Replace(invalidChar, '_');
                }

                if (string.IsNullOrWhiteSpace(fileName) || fileName.Length < 3)
                {
                    string extension = ".exe";
                    if (url.ToLowerInvariant().Contains(".msi")) extension = ".msi";
                    fileName = $"Runtime_{DateTime.Now:yyyyMMddHHmmss}{extension}";
                }

                return fileName;
            }
            catch
            {
                return $"Runtime_{DateTime.Now:yyyyMMddHHmmss}.exe";
            }
        }

        public async Task DownloadFile(string url, string filePath, string runtimeName, IProgress<(string Status, int Progress)> progress)
        {
            try
            {
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    _günlük.Bilgi($"{runtimeName} - İndirme başladı - Toplam Boyut: {totalBytes / 1024.0 / 1024.0:F2} MB");

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long totalBytesRead = 0;
                        int bytesRead;
                        int lastLoggedPercentage = 0;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                var progressPercentage = (int)((totalBytesRead * 100.0) / totalBytes);

                                try
                                {
                                    progress?.Report(($"{runtimeName} İndiriliyor... {progressPercentage}%", progressPercentage));
                                }
                                catch (Exception ex)
                                {
                                    _günlük.Uyarı($"Progress raporlama hatası: {ex.Message}");
                                }

                                if (progressPercentage % 25 == 0 && progressPercentage != lastLoggedPercentage)
                                {
                                    lastLoggedPercentage = progressPercentage;
                                    _günlük.Bilgi($"{runtimeName} - İndirme Durumu: %{progressPercentage}");
                                }
                            }
                        }

                        await fileStream.FlushAsync();
                    }
                }
                _günlük.Bilgi($"{runtimeName} indirmesi tamamlandı");
            }
            catch (OperationCanceledException ex)
            {
                _günlük.Hata($"{runtimeName} indirme zaman aşımına uğradı: {ex.Message}");
                throw new Exception($"{runtimeName} indirme işlemi zaman aşımına uğradı. Lütfen tekrar deneyin.", ex);
            }
            catch (Exception ex)
            {
                _günlük.Hata($"{runtimeName} dosya indirme hatası: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient.Dispose();
                }
                _disposed = true;
            }
        }
    }

    public class RuntimeManager : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _downloadPath;
        private readonly IProgress<(string Status, int Progress)> _progress;
        private readonly GünlükKaydı _günlük;
        private readonly Dictionary<string, Func<Task<string>>> _downloadUrlGetters;
        private DownloadHelper _downloadHelper;
        private bool _disposed = false;

        public RuntimeManager(string downloadPath, IProgress<(string Status, int Progress)> progress)
        {
            _httpClient = new HttpClient();
            _downloadPath = downloadPath;
            _progress = progress;
            _günlük = new GünlükKaydı(Path.GetDirectoryName(downloadPath));

            _downloadUrlGetters = new Dictionary<string, Func<Task<string>>>
        {
            {".NET 6.0 Runtime", () => Task.FromResult(Environment.Is64BitOperatingSystem ?
                "https://aka.ms/dotnet/6.0/windowsdesktop-runtime-win-x64.exe" :
                "https://aka.ms/dotnet/6.0/windowsdesktop-runtime-win-x86.exe")},

            {".NET 7.0 Runtime", () => Task.FromResult(Environment.Is64BitOperatingSystem ?
                "https://aka.ms/dotnet/7.0/windowsdesktop-runtime-win-x64.exe" :
                "https://aka.ms/dotnet/7.0/windowsdesktop-runtime-win-x86.exe")},

            {".NET 8.0 Runtime", () => Task.FromResult(Environment.Is64BitOperatingSystem ?
                "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe" :
                "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe")},

            {"DirectX Runtime", () => Task.FromResult("https://github.com/shadesofdeath/RuntimeMaster/raw/refs/heads/main/directx.exe")},

            {"OpenAL", () => Task.FromResult("https://github.com/shadesofdeath/RuntimeMaster/raw/refs/heads/main/oalinst.exe")},

            {"NVIDIA PhysX", async () => {
            try {
                _günlük.Bilgi("NVIDIA PhysX için en son sürüm kontrol ediliyor...");
                var web = new HtmlWeb();
        
                // İlk siteye git
                var doc = await web.LoadFromWebAsync("https://www.nvidia.com/en-us/drivers/physx-system-software/");
        
                // Download butonunu bul
                var downloadButton = doc.DocumentNode.SelectSingleNode("//div[@class='driverDownloadButtons col4']/a");
                if (downloadButton == null) throw new Exception("PhysX indirme butonu bulunamadı");
        
                // Confirmation URL'sinden direkt indirme linkini oluştur
                var confirmationUrl = downloadButton.GetAttributeValue("href", "");
                _günlük.Bilgi($"Confirmation URL: {confirmationUrl}");
        
                // URL'den dosya yolunu çıkar - basit string işlemi ile
                var urlIndex = confirmationUrl.IndexOf("url=");
                if (urlIndex == -1) throw new Exception("PhysX dosya yolu bulunamadı");

                var filePath = confirmationUrl.Substring(urlIndex + 4); // "url=" sonrasını al
                var ampIndex = filePath.IndexOf("&");
                if (ampIndex != -1) {
                    filePath = filePath.Substring(0, ampIndex); // & işaretine kadar al
                }
        
                // Final download linkini oluştur
                var finalDownloadLink = "//us.download.nvidia.com" + filePath;
                _günlük.Bilgi($"Oluşturulan download linki: {finalDownloadLink}");

                return "https:" + finalDownloadLink;
            }
            catch (Exception ex) {
                _günlük.Hata(ex, "PhysX indirme linki alınırken hata oluştu");
                throw;
            }
        }},

            {"XNA Framework", () => Task.FromResult(
                "https://download.microsoft.com/download/A/C/2/AC2C903B-E6E8-42C2-9FD7-BEBAC362A930/xnafx40_redist.msi")},

            {"Java Runtime", async () => {
                try {
                    _günlük.Bilgi("Java Runtime için en son sürüm kontrol ediliyor...");
                    var web = new HtmlWeb();
                    var doc = await web.LoadFromWebAsync("https://www.java.com/en/download/manual.jsp");
                    var downloadLink = Environment.Is64BitOperatingSystem ?
                        doc.DocumentNode.SelectSingleNode("//a[contains(@title, 'Windows (64-bit)')]")?.GetAttributeValue("href", "") :
                        doc.DocumentNode.SelectSingleNode("//a[contains(@title, 'Windows Offline')]")?.GetAttributeValue("href", "");
                    if (string.IsNullOrEmpty(downloadLink))
                        throw new Exception("Java Runtime indirme linki bulunamadı");
                    return downloadLink;
                }
                catch (Exception ex) {
                    _günlük.Hata(ex, "Java Runtime indirme linki alınırken hata oluştu");
                    throw;
                }
            }},

            {"Visual C++ AIO", async () => {
                try {
                    _günlük.Bilgi("VC++ Redistributables için en son sürüm kontrol ediliyor...");
        
                    // GitHub API için User-Agent header'ı ekliyoruz
                    if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                    {
                        _httpClient.DefaultRequestHeaders.Add("User-Agent", "RuntimeMaster");
                    }

                    // API'yi direkt olarak son release'in dosyalarına yönlendiriyoruz
                    var downloadUrl = Environment.Is64BitOperatingSystem
                        ? "https://github.com/abbodi1406/vcredist/releases/download/v0.85.0/VisualCppRedist_AIO_x86_x64.exe"
                        : "https://github.com/abbodi1406/vcredist/releases/download/v0.85.0/VisualCppRedist_AIO_x86only.exe";

                    // URL'nin geçerli olduğunu kontrol ediyoruz
                    using (var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl))
                    {
                        var response = await _httpClient.SendAsync(request);
                        if (!response.IsSuccessStatusCode)
                        {
                            _günlük.Hata($"İndirme URL'si kontrol edilirken hata: {response.StatusCode}");
                            throw new Exception($"İndirme URL'si geçersiz: {response.StatusCode}");
                        }
                    }

                    _günlük.Bilgi($"VC++ Redistributables indirme linki bulundu: {downloadUrl}");
                    return downloadUrl;
                }
                catch (Exception ex) {
                    _günlük.Hata(ex, "VC++ Redistributables indirme linki alınırken hata oluştu");
                    throw;
                }
            }},

            {"WebView2 Runtime", () => Task.FromResult(Environment.Is64BitOperatingSystem ?
                "https://go.microsoft.com/fwlink/?linkid=2124701" :
                "https://go.microsoft.com/fwlink/?linkid=2099617")}
        };

            _günlük.Bilgi("Runtime Yöneticisi başlatıldı");
            _günlük.Bilgi($"İndirme dizini: {downloadPath}");
            _günlük.Bilgi($"Sistem Mimarisi: {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        }

        public async Task DownloadAndInstallRuntimes(List<string> selectedRuntimes)
        {
            try
            {
                _günlük.Bilgi($"Kurulum başlatılıyor. Seçilen paket sayısı: {selectedRuntimes.Count}");
                _günlük.Bilgi($"Seçilen paketler: {string.Join(", ", selectedRuntimes)}");

                Directory.CreateDirectory(_downloadPath);
                int totalRuntimes = selectedRuntimes.Count;
                int currentRuntime = 0;

                foreach (var runtime in selectedRuntimes)
                {
                    if (_downloadHelper != null)
                    {
                        _downloadHelper.Dispose();
                    }
                    _downloadHelper = new DownloadHelper(_günlük);

                    try
                    {
                        currentRuntime++;
                        _günlük.Bilgi($"Paket işleniyor [{currentRuntime}/{totalRuntimes}]: {runtime}");

                        int baseProgress = ((currentRuntime - 1) * 100) / totalRuntimes;
                        int nextProgress = (currentRuntime * 100) / totalRuntimes;
                        int progressRange = nextProgress - baseProgress;

                        _progress.Report(($"{runtime} indirme hazırlanıyor...", baseProgress));

                        var downloadProgressWrapper = new Progress<(string Status, int Progress)>(p =>
                        {
                            int currentProgress = baseProgress + (p.Progress * progressRange / 200);
                            _progress.Report((p.Status, currentProgress));
                        });

                        string installerPath = await DownloadRuntime(runtime, downloadProgressWrapper);

                        if (!string.IsNullOrEmpty(installerPath))
                        {
                            var installProgressWrapper = new Progress<(string Status, int Progress)>(p =>
                            {
                                int currentProgress = baseProgress + progressRange / 2 + (p.Progress * progressRange / 200);
                                _progress.Report((p.Status, currentProgress));
                            });

                            try
                            {
                                await InstallRuntime(runtime, installerPath, installProgressWrapper);
                            }
                            catch (Exception ex)
                            {
                                _günlük.Hata($"{runtime} kurulumu sırasında hata oluştu: {ex.Message}", ex);
                                // Hata durumunda kurulum dosyasını temizle ve devam et
                                try
                                {
                                    if (File.Exists(installerPath))
                                    {
                                        File.Delete(installerPath);
                                        _günlük.Bilgi($"Hatalı {runtime} kurulum dosyası silindi");
                                    }
                                }
                                catch (Exception cleanupEx)
                                {
                                    _günlük.Uyarı($"Hatalı {runtime} kurulum dosyası silinirken hata: {cleanupEx.Message}");
                                }
                                continue; // Diğer runtime'a geç
                            }

                            await Task.Delay(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        _günlük.Hata($"{runtime} işlemi başarısız oldu: {ex.Message}", ex);
                        continue; // Hata durumunda diğer runtime'a geç
                    }
                }
            }
            finally
            {
                if (_downloadHelper != null)
                {
                    _downloadHelper.Dispose();
                }
            }
        }

        private async Task<string> DownloadRuntime(string runtime, IProgress<(string Status, int Progress)> progress)
        {
            try
            {
                _günlük.Bilgi($"{runtime} için indirme URL'si alınıyor");

                if (!_downloadUrlGetters.ContainsKey(runtime))
                {
                    _günlük.Hata($"{runtime} için indirme yöntemi tanımlanmamış");
                    throw new Exception($"{runtime} için indirme yöntemi tanımlanmamış.");
                }

                string initialUrl = await _downloadUrlGetters[runtime]();
                var (finalUrl, fileName) = await _downloadHelper.ResolveDownloadUrl(initialUrl);

                string filePath = Path.Combine(_downloadPath, fileName);
                _günlük.Bilgi($"{runtime} indirmesi başlatılıyor: {fileName}");

                await _downloadHelper.DownloadFile(finalUrl, filePath, runtime, progress);

                return filePath;
            }
            catch (Exception ex)
            {
                _günlük.Hata($"{runtime} indirme işlemi başarısız oldu", ex);
                throw;
            }
        }
        private bool IsPhysXInstalled()
        {
            try
            {
                _günlük.Bilgi("PhysX kurulum kontrolü yapılıyor...");

                // PhysX'in tipik kurulum yolları
                string[] physXPaths = {
                @"C:\Program Files\NVIDIA Corporation\PhysX\Common",
                @"C:\Program Files (x86)\NVIDIA Corporation\PhysX\Common",
                @"C:\Windows\System32\PhysXCore.dll",
                @"C:\Windows\SysWOW64\PhysXCore.dll"
            };

                // Herhangi bir PhysX yolu varsa kurulu kabul et
                foreach (string path in physXPaths)
                {
                    if (Directory.Exists(path) || File.Exists(path))
                    {
                        _günlük.Bilgi($"PhysX kurulumu bulundu: {path}");
                        return true;
                    }
                }

                _günlük.Bilgi("PhysX kurulu değil");
                return false;
            }
            catch (Exception ex)
            {
                _günlük.Hata("PhysX kurulum kontrolü sırasında hata oluştu", ex);
                return false;
            }
        }
        private async Task InstallRuntime(string runtime, string installerPath, IProgress<(string Status, int Progress)> progress)
        {
            try
            {
                if (runtime == "NVIDIA PhysX" && IsPhysXInstalled())
                {
                    _günlük.Bilgi("PhysX zaten kurulu olduğu için kurulum atlanıyor");
                    progress.Report(("PhysX zaten kurulu. Kurulum atlanıyor...", 100));

                    if (File.Exists(installerPath))
                    {
                        try
                        {
                            File.Delete(installerPath);
                            _günlük.Bilgi("PhysX kurulum dosyası silindi");
                        }
                        catch (Exception ex)
                        {
                            _günlük.Uyarı($"PhysX kurulum dosyası silinirken hata: {ex.Message}");
                        }
                    }
                    return;
                }

                _günlük.Bilgi($"{runtime} kurulumu başlatılıyor. Kurulum dosyası: {installerPath}");
                progress.Report(($"{runtime} Kuruluyor... (Lütfen Bekleyiniz)", 0));

                string arguments = GetInstallArguments(runtime);
                _günlük.Bilgi($"{runtime} kurulum parametreleri: {arguments}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        _günlük.Bilgi($"{runtime} kurulum işlemi başladı. İşlem ID: {process.Id}");
                        progress.Report(($"{runtime} Kuruluyor...", 50));

                        await Task.Run(() => process.WaitForExit());
                        progress.Report(($"{runtime} Kurulum Tamamlanıyor...", 90));

                        _günlük.Bilgi($"{runtime} kurulum işlemi tamamlandı. Çıkış kodu: {process.ExitCode}");

                        if (runtime != "DirectX Runtime" && process.ExitCode != 0)
                        {
                            throw new Exception($"{runtime} kurulumu başarısız oldu. (Çıkış kodu: {process.ExitCode})");
                        }
                    }
                    else
                    {
                        throw new Exception($"{runtime} kurulum işlemi başlatılamadı.");
                    }
                }

                // Kurulum dosyasını temizle
                progress.Report(($"{runtime} Temizleniyor...", 95));
                try
                {
                    if (File.Exists(installerPath))
                    {
                        File.Delete(installerPath);
                        _günlük.Bilgi($"{runtime} kurulum dosyası silindi");
                    }
                }
                catch (Exception ex)
                {
                    _günlük.Uyarı($"{runtime} kurulum dosyası silinirken hata: {ex.Message}");
                }

                progress.Report(($"{runtime} Kurulum Tamamlandı", 100));
                _günlük.Bilgi($"{runtime} kurulumu başarıyla tamamlandı");
            }
            catch (Exception ex)
            {
                _günlük.Hata($"{runtime} kurulum işlemi başarısız oldu: {ex.Message}", ex);
                throw; // Üst katmanda yakalanacak
            }
        }

        private string GetInstallArguments(string runtime)
        {
            string arguments = "/quiet";

            if (runtime.Contains(".NET"))
            {
                arguments = "/install /quiet /norestart";
            }
            else if (runtime == "DirectX Runtime")
            {
                arguments = "";
            }
            else if (runtime == "OpenAL")
            {
                arguments = "/SILENT";
            }
            else if (runtime == "NVIDIA PhysX")
            {
                arguments = "/s /v\"/qn REBOOT=ReallySuppress\"";
            }
            else if (runtime == "Visual C++ AIO")
            {
                arguments = "/ai";
            }
            else if (runtime == "WebView2 Runtime")
            {
                arguments = "/silent /install";
            }
            else if (runtime == "XNA Framework")
            {
                arguments = "/quiet /norestart";
            }
            else if (runtime == "Java Runtime")
            {
                arguments = "/s";
            }

            _günlük.Bilgi($"{runtime} için kurulum parametreleri: {arguments}");
            return arguments;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _downloadHelper?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    
    public partial class MainWindow : Window
    {
        private RuntimeManager _runtimeManager;
        private Progress<(string Status, int Progress)> _progress;

        public MainWindow()
        {
            InitializeComponent();
            this.Hide(); // Hide main window initially
            InitializeAsync();
        }
        private void PositionWindow()
        {
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Right - Width - 10;
            Top = workingArea.Bottom - Height - 10;
        }
        private async void InitializeAsync()
        {
            try
            {
                // Show runtime selection window first
                var selectionWindow = new RuntimeSelectionWindow();
                var result = selectionWindow.ShowDialog();

                if (result == true && selectionWindow.SelectedRuntimes.Count > 0)
                {
                    // Position and show main window if user selected runtimes and clicked Install
                    PositionWindow();
                    this.Show();
                    await InstallRuntimes(selectionWindow.SelectedRuntimes);
                }
                else
                {
                    // If user cancels or doesn't select any runtimes, close the application
                    Application.Current.Shutdown();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Başlatma hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }
        private async Task InstallRuntimes(List<string> selectedRuntimes)
        {
            try
            {
                string downloadPath = Path.Combine(Path.GetTempPath(), "RuntimeMaster");

                _progress = new Progress<(string Status, int Progress)>(UpdateProgress);
                _runtimeManager = new RuntimeManager(downloadPath, _progress);

                await _runtimeManager.DownloadAndInstallRuntimes(selectedRuntimes);

                // Installation completed
                InstallStatus.Text = "Kurulum Tamamlandı!";
                await Task.Delay(2000); // Show completion message for 2 seconds
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kurulum hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void UpdateProgress((string Status, int Progress) progress)
        {
            // Ensure UI updates happen on UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(progress));
                return;
            }

            InstallStatus.Text = progress.Status;
            ProgressBar.Value = progress.Progress;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _runtimeManager?.Dispose();
        }
    }
}