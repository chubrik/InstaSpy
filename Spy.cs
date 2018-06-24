using Kit;
using Kit.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;

namespace InstaSpy
{
    public class Spy
    {
        private const string MainUrl = "https://www.instagram.com/";
        private const string LoginUrl = "https://www.instagram.com/accounts/login/ajax/";
        private readonly string _userName;
        private readonly string _password;

        private readonly TimeSpan _repeatTimeSpan = TimeSpan.FromSeconds(60);
        private List<string> _previousUrls = new List<string>();
        private readonly HttpClient _http = new HttpClient();
        private int _counter = 0;
        private string _dataUrl;

        public Spy(string userName, string password)
        {
            _userName = userName;
            _password = password;
        }

        public void Run()
        {
            foreach (var name in FileClient.FileNames(""))
                FileClient.Delete(name);

            var mainHtml = LoginAndGetMailHtml();
            _dataUrl = MainUrl + Regex.Match(mainHtml, @"(?<=""preload"" href=""/)[^""]+(?="" as=)").Value;

            var nextTime = DateTimeOffset.Now;

            while (true)
            {
                nextTime += _repeatTimeSpan;
                Work();
                var delay = nextTime - DateTimeOffset.Now;

                if (delay.Milliseconds > 0)
                    Task.Delay(delay).Wait();
                else
                    nextTime = DateTimeOffset.Now;
            }
        }

        private Dictionary<string, List<string>> _items = new Dictionary<string, List<string>>();

        private void Work()
        {
            LogService.LogInfo($"Step {++_counter}");
            var serializedData = _http.GetText(_dataUrl);
            dynamic data = JsonConvert.DeserializeObject(serializedData);
            var edges = data["data"]["user"]["edge_web_feed_timeline"]["edges"];
            var newUrls = new List<string>();

            foreach (var edge in edges)
            {
                var node = edge["node"];
                var code = (string)node["shortcode"];

                if (_items.ContainsKey(code))
                    continue;

                var names = new List<string>();
                var children = node["edge_sidecar_to_children"];

                if (children != null)
                {
                    var childEdges = children["edges"];

                    foreach (var childEdge in childEdges)
                    {
                        var url = (string)childEdge["node"]["display_url"];
                        newUrls.Add(url);
                        names.Add(PathHelper.FileName(url));
                    }
                }
                else
                {
                    var url = (string)node["display_url"];
                    newUrls.Add(url);
                    names.Add(PathHelper.FileName(url));
                }

                _items[code] = names;
                var timer = new Timer(5 * 60000); // 5 min
                timer.Elapsed += (sender, e) => Check(code, timer, 1);
                timer.AutoReset = false;
                timer.Start();
            }

            foreach (var newUrl in newUrls)
            {
                var name = PathHelper.FileName(newUrl);

                if (!FileClient.Exists(name))
                {
                    var bytes = _http.GetBytes(newUrl);
                    FileClient.Write(name, bytes);
                }
            }
        }

        private void Check(string code, Timer timer, int step)
        {
            timer.Dispose();
            var url = $"{MainUrl}p/{code}/";
            var response = _http.Get(url);

            if (response.StatusCode == 200)
            {
                if (step == 3)
                {
                    foreach (var name in _items[code])
                        FileClient.Delete(name);

                    _items.Remove(code);
                    return;
                }

                step++;

                var interval = step == 2
                    ? 55 * 60000 // 55 min
                    : 11 * 60 * 60000; // 11 hours

                timer = new Timer(interval);
                timer.Elapsed += (sender, e) => Check(code, timer, step);
                timer.AutoReset = false;
                timer.Start();
            }
            else
            {
                var names = _items[code];
                ReportService.Report("InstaSpy captured!", $"Captured {names.Count} photos on step {step}:", names);

                foreach (var name in names)
                    FileClient.Delete(name);

                _items.Remove(code);
            }
        }

        private string LoginAndGetMailHtml()
        {
            var html = _http.GetText(MainUrl);

            if (html.Contains($"\"{_userName}\""))
                return html;

            var token = Regex.Match(html, @"(?<=""csrf_token"":"")[^""]+(?="")").Value;
            _http.SetHeader("X-CSRFToken", token);
            _http.SetHeader("X-Instagram-AJAX", "1");

            _http.PostForm(LoginUrl,
                new Dictionary<string, string> {
                    { "username", _userName },
                    { "password", _password }
                });

            html = _http.GetText(MainUrl);

            if (html.Contains($"\"{_userName}\""))
                return html;

            throw new Exception("Login is failed");
        }
    }
}
