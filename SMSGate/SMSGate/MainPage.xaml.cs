using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Plugin.Messaging;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace SMSGate
{
    public partial class MainPage : ContentPage
    {
        private bool serverListening;
        private TcpListener server;
        private int serverPort = 5005;

        private readonly string savePath = $"{Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData)}/saveFile.txt";
        private bool applicationLoaded;
        public MainPage()
        {
            InitializeComponent();

            Log("Application started");

            Appearing += MainPage_Appearing;
            Disappearing += MainPage_Disappearing;

            applicationLoaded = true;

            if (File.Exists(savePath))
            {
                try
                {
                    var settings = JObject.Parse(File.ReadAllText(savePath));
                    PortBox.Text = settings["serverPort"].ToString();
                    EnabledSwitch.IsToggled = settings["serverEnabled"].ToString().ToLower() == "true";

                    Log("Save file loaded");
                }
                catch (Exception e)
                {
                    Log("Save file failed to load completly:");
                    Log(e);
                }
            }
        }

        private void MainPage_Disappearing(object sender, EventArgs e)
        {
            server.Stop();
        }

        private async void MainPage_Appearing(object sender, EventArgs e)
        {
            if (await Permissions.RequestAsync<Permissions.Sms>() is not PermissionStatus.Granted ||
                await Permissions.RequestAsync<Permissions.NetworkState>() is not PermissionStatus.Granted)
            {
                Environment.Exit(-1);
            }

            while (IsVisible)
            {
                while (serverListening)
                {
                    try
                    {
                        ipPortBox.Text = $"{GetLocalIP()}:{(server.Server.LocalEndPoint as IPEndPoint).Port}";
                    }
                    catch (Exception exception)
                    {
                        Log(exception);
                    }

                    try
                    {
                        var client = await server.AcceptTcpClientAsync();
                        Log($"Request from {(client.Client.RemoteEndPoint as IPEndPoint).Address.MapToIPv4()}");
                        var stream = client.GetStream();
                        var reader = new StreamReader(stream);
                        var writer = new StreamWriter(stream);

                        try
                        {
                            Log("Reading request...");
                            var jsonStr = await reader.ReadLineAsync();
                            Log("Parcing request...");
                            var jsonObj = JObject.Parse(jsonStr);

                            switch (jsonObj["type"].ToString())
                            {
                                case "send":
                                {
                                    var to = jsonObj["to"].ToString();
                                    var message = jsonObj["msg"].ToString();
                                    Log($"Sending message to {to}..");
                                    CrossMessaging.Current.SmsMessenger.SendSmsInBackground(to, message);
                                    Log("Success!");

                                    await writer.WriteLineAsync(new JObject
                                    {
                                        ["success"] = "true"
                                    }.ToString()
                                        .Replace("\n", "")
                                        .Replace("\r", ""));
                                    break;
                                }
                            }

                        }
                        catch (Exception exception)
                        {
                            Log(exception);
                            await writer.WriteLineAsync(new JObject
                            {
                                ["success"] = "false",
                                ["error"] = exception.ToString()
                            }
                            .ToString()
                                .Replace("\n", "")
                                .Replace("\r", ""));
                        }

                        await writer.FlushAsync();
                        client.Close();
                    }
                    catch (ObjectDisposedException)
                    {
                        //Server closed
                    }
                    catch (Exception exception)
                    {
                        Log(exception);
                    }
                }

                if (!serverListening)
                {
                    ipPortBox.Text = "None";
                }

                await Task.Delay(100);
            }
        }

        private void PortBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!applicationLoaded)
            {
                return;
            }


            if (int.TryParse(PortBox.Text, out var i))
            {
                serverPort = i;
                SaveAll();
            }
        }

        private void ClearLogsButton_OnClicked(object sender, EventArgs e)
        {
            logsBox.Text = "";
        }

        private void EnabledSwitch_OnToggled(object sender, ToggledEventArgs e)
        {
            if (!applicationLoaded)
            {
                return;
            }


            if (serverListening)
            {
                if (!e.Value)
                {
                    server.Stop();
                    serverListening = false;
                    Log("Server stopped");
                }
            }
            else
            {
                if (e.Value)
                {
                    server = TcpListener.Create(serverPort);
                    server.Start();
                    serverListening = true;
                    Log("Server started");
                }
            }

            SaveAll();
        }

        private string GetLocalIP()
        {
            foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                return address.MapToIPv4().ToString();
            }

            return "0.0.0.0";
        }

        private void Log(object o)
        {
            var newLineLog = $"[{DateTime.Now.ToShortTimeString()}] {o}\n";
            Console.Write(newLineLog);

            logsBox.Text += newLineLog;
            ScrollView.ScrollToAsync(EndLabel, ScrollToPosition.End, true);
        }

        private void SaveAll()
        {
            if (!applicationLoaded)
            {
                return;
            }

            var settings = new JObject
            {
                ["serverPort"] = serverPort.ToString(), 
                ["serverEnabled"] = serverListening
            };

            File.WriteAllText(savePath, settings.ToString());
        }
    }
}
