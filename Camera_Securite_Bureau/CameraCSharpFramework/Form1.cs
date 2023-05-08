using Accord.Video.DirectShow;
using Accord.Video;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using Accord.Video.FFMPEG;
using System.Threading;
using System.Data.SqlClient;
using System.Net;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Exceptions;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace CameraCSharpFramework
{
    public partial class camera_securite_bureau : Form
    {
        //constante pour connecté a mqtt et au topic
        public static string nomServeur = "test.mosquitto.org";
        public static int portServeur = 1883;
        private static IMqttClient _mqttClient;
        private static string nomTopic = "capteur_ultrason";
        //------------------------------------------------------
        private Bitmap image;
        private Bitmap resizedImage;
        //variable pour dire aux thread du socket darreter de fonctionner
        private CancellationTokenSource broadcastCancellationTokenSource;
        //Trouve les cameras sur l'ordinateur
        private FilterInfoCollection videoDevices;
        //driver directShow
        private VideoCaptureDevice videoCaptureDevice;
        //donné à envoyer au socket
        private static string imageBase64 = string.Empty;
        //Connection du client
        private static List<ClientConnection> clients = new List<ClientConnection>();
        
        //Calcule le nombre de frame a enregistré
        private static int recordedFrames = 0;
        private const int framesPerSecond = 30;
        private const int desiredVideoLengthSeconds = 1 * 10; // 5 minutes in seconds
        private const int totalFramesToRecord = framesPerSecond * desiredVideoLengthSeconds;
        private static int pictureBoxWidth = 0;
        private static int pictureBoxHeight = 0;

        private static byte[] thumbnail = null;
        private static VideoFileWriter videoFileWriter;
        private static string nomVideo = "temp.mp4";
        //quand recois message MQTT mettre true
        private static bool videoRecording = false;
        private static string receivedMessage;


        string connectionString = "Server=PAUM;Database=maison_connecte;User Id=userMaison;Password=123Maison.;";

        private static readonly object _syncLock = new object();

        public camera_securite_bureau()
        {
            InitializeComponent();
        }

        //souscrit au topic et va lire les valeurs. Commence l'enregistrement lorsque porte ouverte.
        public static async Task Subscribe_Topic()
        {
            try
            {
                var mqttFactory = new MqttFactory();
                _mqttClient = mqttFactory.CreateMqttClient();

                var mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer(nomServeur, 1883).Build();

                _mqttClient.ApplicationMessageReceivedAsync += (e) =>
                {
                    receivedMessage = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                    Debug.WriteLine($"Received message from topic '{e.ApplicationMessage.Topic}': {receivedMessage}");

                    return Task.CompletedTask;
                };

                await _mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);

                var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(
                        f =>
                        {
                            f.WithTopic(nomTopic);
                        })
                    .Build();

                var response = await _mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);

                Debug.WriteLine("MQTT client subscribed to topic.");
            }
            catch (MqttCommunicationException ex)
            {
                Debug.WriteLine("An error occurred while communicating with the MQTT broker: " + ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("An error occurred: " + ex.Message);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            pictureBoxWidth = pictureBox1.Width;
            pictureBoxHeight = pictureBox1.Height;
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            foreach (FilterInfo filterInfo in videoDevices)
            {
                comboBox1.Items.Add(filterInfo.Name);
            }
            comboBox1.SelectedIndex = 0;

            videoCaptureDevice = new VideoCaptureDevice();

            CreateSocket();

            videoCaptureDevice = new VideoCaptureDevice(videoDevices[0].MonikerString);
            videoCaptureDevice.NewFrame += new NewFrameEventHandler(VideoCaptureDevice_NewFrame);
            videoCaptureDevice.Start();
            // Initialize VideoFileWriter
            videoFileWriter = new VideoFileWriter();

            Task.Run(async () =>
            {
                await Subscribe_Topic();
            });
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (videoCaptureDevice != null && videoCaptureDevice.IsRunning)
            {
                videoCaptureDevice.SignalToStop();
                videoCaptureDevice.WaitForStop();
                videoCaptureDevice = null;
            }

            if (videoFileWriter != null)
            {
                videoFileWriter.Close();
                videoFileWriter.Dispose();
            }

            StopBroadcastTask();
        }

        private static void StartRecording()
        {
            // Reset the recordedFrames counter
            recordedFrames = 0;
            videoRecording = true;
            string outputFilePath = nomVideo;

            videoFileWriter.Open(outputFilePath, 640, 480, framesPerSecond, VideoCodec.Default, 1000000);      
        }

        private void debugThread()
        {
            Process process = Process.GetCurrentProcess();
            Debug.WriteLine("Active threads when closing the form:");
            foreach (ProcessThread thread in process.Threads)
            {
                Debug.WriteLine($"Thread ID: {thread.Id}, State: {thread.ThreadState}, Start Time: {thread.StartTime}");
            }
        }

        private void StopBroadcastTask()
        {
            if (broadcastCancellationTokenSource != null)
            {
                broadcastCancellationTokenSource.Cancel();
                broadcastCancellationTokenSource.Dispose();
                broadcastCancellationTokenSource = null;
            }
        }
        public static void Broadcast()
        {
            //Debug.WriteLine("broadcasting");

            // Add the "---END_OF_FRAME---" delimiter to imageBase64
            string imageBase64WithDelimiter = imageBase64 + "---END_OF_FRAME---";

            byte[] messageBytes = Encoding.ASCII.GetBytes(imageBase64WithDelimiter);
            ClientConnection connection;

            lock (_syncLock)
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    if (i >= clients.Count)
                    {
                        continue;
                    }
                    connection = clients[i];
                    try
                    {
                        connection.ClientSocket.Send(messageBytes);
                    }
                    catch (SocketException)
                    {
                        HandleClientConnection(connection);
                    }
                }
            }
        }

        private static void HandleClientConnection(ClientConnection connection)
        {
            Debug.WriteLine($"Client connection: {connection}");
            // Handle the client connection here
            // (e.g., receive data, process requests, etc.)
            // This example only shows broadcasting a string to clients

            // Cleanup
            //Debug.WriteLine("remove client");
            connection.ClientSocket.Close();
            lock (_syncLock)
            {
                clients.Remove(connection);
            }
        }


        private void CreateSocket()
        {
            Debug.WriteLine("Socket created");
            broadcastCancellationTokenSource = new CancellationTokenSource();
            var token = broadcastCancellationTokenSource.Token;

            Task.Run(() =>
            {
                int port = 8010;
                IPAddress ipAddress = IPAddress.Any;

                Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);
                serverSocket.Bind(localEndPoint);
                serverSocket.Listen(10);

                Debug.WriteLine($"Server is listening on {ipAddress}:{port}");

                while (true)
                {
                    Socket clientSocket = serverSocket.Accept();
                    Debug.WriteLine("Client connected.");

                    ClientConnection connection = new ClientConnection { ClientSocket = clientSocket };

                    lock (_syncLock)
                    {
                        clients.Add(connection);
                    }
                }
            });

            //serverSocket.DataReceived += ServerSocket_DataReceived;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    Broadcast();
                    await Task.Delay(1000 / 30);
                }
            });
        }
        private void button1_Click(object sender, EventArgs e)
        {
            if (videoCaptureDevice != null && videoCaptureDevice.IsRunning)
            {
                videoCaptureDevice.SignalToStop();
                videoCaptureDevice.WaitForStop();
                videoCaptureDevice = null;
            }
            videoCaptureDevice = new VideoCaptureDevice(videoDevices[comboBox1.SelectedIndex].MonikerString);
            videoCaptureDevice.NewFrame += new NewFrameEventHandler(VideoCaptureDevice_NewFrame);
            videoCaptureDevice.Start();
            StartRecording();
        }
        private void StopRecording()
        {
            videoRecording = false;
            receivedMessage = "";
            // Close the VideoFileWriter instance
            videoFileWriter.Close();
        }
        static byte[] GetVideoBytes(string videoFilePath)
        {
            using (FileStream fileStream = new FileStream(videoFilePath, FileMode.Open, FileAccess.Read))
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    fileStream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }
        }

        private void VideoCaptureDevice_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            image = (Bitmap)eventArgs.Frame.Clone();

            if (videoRecording == false && receivedMessage == "1")
            {
                StartRecording();
            }

            // Stop recording after reaching the desired number of frames
            if (recordedFrames == totalFramesToRecord && videoRecording == true)
            {
                StopRecording();
                sauvegardeVideoDansBD();
            }
            //rendu à moitier ont enregistre le thumbnail et envois dans BD
            else if (recordedFrames == (totalFramesToRecord/2))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    //sauvegarde l'image dans le buffer
                    image.Save(ms, ImageFormat.Jpeg);

                    //convertit le memory stream dans un byte array
                    thumbnail = ms.ToArray();
                }
            }

            if (videoRecording == true)
            {
                recordedFrames++;
                // Save frames to the VideoFileWriter while capturing frames
                videoFileWriter.WriteVideoFrame(image);
            }

            resizedImage = new Bitmap(pictureBoxWidth, pictureBoxHeight);

            using (Graphics graphics = Graphics.FromImage(resizedImage))
            {
                graphics.DrawImage(image, 0, 0, pictureBoxWidth, pictureBoxHeight);
            }
            
            conversionToBase64(image);
                
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = resizedImage;
          
        }
        private void conversionToBase64(Bitmap image)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Jpeg);

                byte[] imageBytes = ms.ToArray();

                string base64string = Convert.ToBase64String(imageBytes);
                byte[] utf8bytes = System.Text.Encoding.UTF8.GetBytes(base64string);

                imageBase64 = System.Text.Encoding.ASCII.GetString(utf8bytes);
            }
            image.Dispose();
        }

        private void sauvegardeVideoDansBD()
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                {
                    connection.Open();

                    byte[] videoBytes = GetVideoBytes(nomVideo);

                    // Execute a simple query
                    var context = new maison_connecte2Entities();

                    // Create a new enregistrements object
                    var newRecord = new enregistrement
                    {
                        flux_video = videoBytes,
                        thumbnail = thumbnail,
                    };

                    // Add the new enregistrements object to the enregistrements DbSet
                    context.enregistrements.Add(newRecord);

                    // Save changes to the database
                    context.SaveChanges();
                }
            }
           
            try
                {
                    File.Delete(nomVideo);
                    Debug.WriteLine("File deleted successfully.");
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"Error deleting file: {ex.Message}");
                }
        }
    }
}
