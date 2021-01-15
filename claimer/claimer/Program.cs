using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace epic_claimer
{
    internal static class Program
    {
        // webdriver stuff
        private static IWebDriver _driver;
        private static WebDriverWait _wait;

        // api url for sending telegram messages.
        private const string Url = "https://epic-games-yoinker-api.azurewebsites.net/message/send";

        // user variables
        private static string _username;
        private static string _password;
        private static string _captcha;
        private static string _telegram;


        private static void Main(string[] args)
        {
            // Retrieve the user variables, these should be set through github secrets
            _username = Environment.GetEnvironmentVariable("epicname");
            _password = Environment.GetEnvironmentVariable("epicpass");
            _captcha  = Environment.GetEnvironmentVariable("captcha");
            
            // optional: search in telegram for the bot "epic games yoinker, send him a message after a while he send you the id"
            _telegram = Environment.GetEnvironmentVariable("telegram");

            // Check if the arguments are valid.
            if (ValidateArguments() == false)
            {
                return;
            }
            
            // create an instance of the webdriver
            _driver = new ChromeDriver();
            // create an instance of the webdriver waiter.
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(50));
            // maximize the window.
            _driver.Manage().Window.Maximize();

            // if the cookie was not retrieved successfully.
            if (GetCookie(_captcha) == false)
            {
                Console.WriteLine("Failed to retrieve authentication cookie.");
                return;
            }

            // Try to login.
            if (Login(_username, _password) == false)
            {
                return;
            }

            Thread.Sleep(5000);

            // Retrieve the game urls.
            foreach (var url in GetFreeGamesUrls())
            {
                var status = Status.Failed;
                
                for (var i = 0; i < 5; i++)
                {
                    status = ClaimGame(url);
                    
                    if (status == Status.Success)
                    {
                        SendTelegram(url, status);
                        break;
                    }
                    if (status == Status.Owned)
                    {
                        break;
                    }
                }
                if (status == Status.Failed)
                {
                    SendTelegram(url,status);
                }
            }

            Console.WriteLine("process finished");
        }

        private static bool ValidateArguments()
        {
            if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password) || string.IsNullOrEmpty(_captcha))
            {
                Console.WriteLine("missing arguments");

                return false;
            }
            try
            {
                var mailAddress = new MailAddress(_username);
            }
            catch (FormatException)
            {
                Console.WriteLine("email address is not valid");

                return false;
            }

            if (new Regex("^https:\\/\\/accounts.hcaptcha.com\\/verify_email\\/[0-9a-z-]+$").IsMatch(_captcha) == false)
            {
                Console.WriteLine("captcha url is not valid");

                return false;
            }


            return true;
        }

        private static bool GetCookie(string url)
        {
            const int maxTries = 5;

            _driver.Navigate().GoToUrl(url);

            Thread.Sleep(5000);

            for (var i = 0; i < maxTries; i++)
            {
                Console.Write($"{i + 1}/{maxTries} retrieving cookie : ");

                GetElement("//button[@title=\"Click to set accessibility cookie\"]").Click();

                Thread.Sleep(7500);


                if (_driver.PageSource.Contains("Cookie set."))
                {
                    Console.WriteLine("success");

                    return true;
                }

                Console.WriteLine("failed");
            }

            return false;
        }

        private static void AddEpicCookies()
        {
            _driver.Manage().Cookies.AddCookie(new Cookie(
                name: "HAS_ACCEPTED_AGE_GATE_ONCE",
                value: "true",
                domain: "www.epicgames.com",
                path: "/",
                expiry: DateTime.Now.AddHours(1)
            ));
            _driver.Manage().Cookies.AddCookie(new Cookie(
                name: "OptanonAlertBoxClosed",
                value: "en-US",
                domain: ".epicgames.com",
                path: "/",
                expiry: DateTime.Now.AddHours(1)
            ));
        }

        private static bool Login(string user, string pass)
        {
            const int maxTries = 15;

            _driver.Navigate().GoToUrl("https://www.epicgames.com/id/login/");

            GetElement("//div[@aria-label=\"Sign in with Epic Games\"]").Click();

            AddEpicCookies();

            var loginUrl = _driver.Url;

            for (var i = 0; i < maxTries; i++)
            {
                Console.Write($"{i + 1}/{maxTries} Logging in : ");

                Thread.Sleep(2000);

                var nameField = GetElement("//input[@name=\"email\"]");
                var passField = GetElement("//input[@name=\"password\"]");

                nameField.Clear();
                passField.Clear();

                nameField.SendKeys(user);
                passField.SendKeys(pass);

                Thread.Sleep(1000);

 
                if (_driver.Url != loginUrl)
                {
                    Console.WriteLine("success");

                    return true;
                }

                try
                {
                    GetElement("//span[text()=\"Log in now\"]").Click();
                    
                    Thread.Sleep(20000);
                }
                catch
                {
                    // ignored
                }
                
                if (_driver.Url != loginUrl)
                {
                    Console.WriteLine("success");

                    return true;
                }

                Console.WriteLine("failed");
            }

            return false;
        }

        private static IEnumerable<string> GetFreeGamesUrls()
        {
            _driver.Navigate().GoToUrl("https://www.epicgames.com/store/en-US/free-games");

            _wait.Until(x => x.FindElement(By.XPath("//div[@data-component=\"CardGridDesktopBase\"]")).Displayed);

            Thread.Sleep(10000);

            var urls = GetElements("//a[descendant::span[text()='Free Now']]")
                .Select(element => element.GetAttribute("href")).ToList();

            return urls;
        }

        private static Status ClaimGame(string url)
        {
            Console.Write($"claiming {url} : ");

            _driver.Navigate().GoToUrl(url);

            Thread.Sleep(5000);

            if (_driver.PageSource.Contains("Owned</span>"))
            {
                Console.WriteLine("already owned");
                
                return Status.Owned;
            }

            try
            {
                // Click the get button.
                GetElement("//button[@data-testid=\"purchase-cta-button\"]").Click();
                Thread.Sleep(20000);
                if (_driver.PageSource.ToLower().Contains("please read this agreement carefully"))
                {
                    GetElement("//input[@id=\"agree\"]").Click();
                    Thread.Sleep(1000);
                    GetElement("//button[descendant::span[text()='Accept']]").Click();
                    Thread.Sleep(1000);
                    GetElement("//button[@data-testid=\"purchase-cta-button\"]").Click();
                    Thread.Sleep(20000);
                }
                
                // Click place order button
                GetElement("//button[@class=\"btn btn-primary\"]").Click();
                Thread.Sleep(20000);
                // click the agree button
                GetElements("//button[@class=\"btn btn-primary\"]")[1].Click();
                Thread.Sleep(5000);
                
                Console.WriteLine("Claimed");
                return Status.Success;
            }
            catch
            {
                Console.WriteLine("Failed");
                
                return Status.Failed;
            }
        }

        private static List<IWebElement> GetElements(string xPath)
        {
            try
            {
                _wait.Until(x => x.FindElements(By.XPath(xPath)));

                return _driver.FindElements(By.XPath(xPath)).ToList();
            }
            catch (Exception e)
            {
                Console.WriteLine($"element not found : {xPath}");

                throw;
            }
        }

        private static IWebElement GetElement(string xPath)
        {
            try
            {
                _wait.Until(x => x.FindElement(By.XPath(xPath)));
                
                

                return _driver.FindElement(By.XPath(xPath));
            }
            catch
            {
                return null;
            }
        }

        private static void SendTelegram(string url, Status status)
        {
            Console.Write("Sending telegram message... ");
            if (string.IsNullOrEmpty(_telegram))
            {
                return;
            }
            try
            {
                var messageData = JsonConvert.SerializeObject(new
                {
                    Id      = Convert.ToInt32(_telegram),
                    Url     = url,
                    Status  = status,  
                });
                new HttpClient().PostAsync(Url, new StringContent(
                    messageData,
                    Encoding.UTF8,
                    "application/json"
                )).Wait();
                Console.WriteLine("success.");
            }
            catch
            {
                Console.WriteLine("failed.");
            }
        }
    }

    public enum Status
    {
        // only status 0 and 1 can be passed to the telegram api.
        Success = 0,
        Failed  = 1,
        Owned = 2,
    }
}