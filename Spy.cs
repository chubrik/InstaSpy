using Kit;
using Kit.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

        public Spy(string userName, string password)
        {
            _userName = userName;
            _password = password;
        }

        public void Run()
        {
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

        private void Work()
        {
            LogService.LogInfo($"{++_counter}");
            var mainHtml = LoginAndGetMailHtml();
            var dataUrl = MainUrl + Regex.Match(mainHtml, @"(?<=""preload"" href=""/)[^""]+(?="" as=)").Value;
            var data = _http.GetText(dataUrl);
            var matches = Regex.Matches(data, "\"display_url\": ?\"([^\"]+)\"");
            var actualUrls = (from Match match in matches select match.Groups[1].Value).Distinct().ToList();
            var actualNames = actualUrls.Select(PathHelper.FileName).ToList();
            var capturedUrls = new List<string>();
            var skipLast = true;

            for (var i = _previousUrls.Count - 1; i >= 0; i--)
            {
                var previousUrl = _previousUrls[i];
                var previousName = PathHelper.FileName(previousUrl);

                if (skipLast)
                {
                    if (actualNames.Contains(previousName))
                        skipLast = false;
                }
                else if (!actualNames.Contains(previousName))
                    capturedUrls.Add(previousUrl);
            }

            foreach (var actualUrl in actualUrls)
            {
                var actualName = PathHelper.FileName(actualUrl);

                if (FileClient.Exists(actualName))
                    continue;

                var imageBytes = _http.GetBytes(actualUrl);
                FileClient.Write(actualName, imageBytes);
            }

            if (capturedUrls.Count > 0)
            {
                var capturedNames = capturedUrls.Select(PathHelper.FileName).ToList();

                foreach (var capturedName in capturedNames)
                    LogService.LogSuccess($"Captured: {capturedName}");

                ReportService.Report("InstaSpy captured!", $"Captured photos ({capturedNames.Count}):", capturedNames);
            }

            var existsNames = FileClient.FileNames();

            foreach (var existsName in existsNames)
                if (!actualNames.Contains(existsName))
                    FileClient.Delete(existsName);

            _previousUrls = actualUrls;
        }

        private string LoginAndGetMailHtml()
        {
            var response = _http.Get(MainUrl);
            var html = response.GetText();

            if (html.Contains($"\"{_userName}\""))
                return html;

            var token = response.Headers.GetValue("Set-Cookie").First(i => i.StartsWith("csrftoken=")).Substring(10, 32);
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
