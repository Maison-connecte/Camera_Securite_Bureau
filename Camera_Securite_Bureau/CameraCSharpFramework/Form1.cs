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
using System.Timers;
using Accord.Video.FFMPEG;
using System.Threading;
using System.Data.SqlClient;
using System.Net;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace CameraCSharpFramework
{
    public partial class Form1 : Form
    {
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
        //connection à la BD
        string connectionString = "Server=PAUM\\PAUM;Database=maison_connecte;User Id=userMaison;Password=123Maison.;";
        //string connectionString = "Server=MAXIME_PAULIN\\SQLEXPRESS;Database=maison_connecte;User Id=userMaison;Password=123Maison.;"
        //string connectionString = "Server=PAUM;Database=maison_connecte;User Id=userMaison;Password=123Maison.;";
        //string connectionString = "Server=localhost;User ID=thugapy;Password=testpw;Database=MaisonConnecte;Trusted_Connection=False;Encrypt=False";
        //Calcule le nombre de frame a enregistré
        private int recordedFrames = 0;
        private const int framesPerSecond = 30;
        private const int desiredVideoLengthSeconds = 1 * 10; // 5 minutes in seconds
        private const int totalFramesToRecord = framesPerSecond * desiredVideoLengthSeconds;
        private int pictureBoxWidth = 0;
        private int pictureBoxHeight = 0;


        private VideoFileWriter videoFileWriter;
        private string nomVideo = "temp.mp4";
        private bool videoFiniEnregistre = false;
        //quand recois message MQTT mettre true
        private bool videoRecording = false;

        private static readonly object _syncLock = new object();

        public Form1()
        {
            InitializeComponent();
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

        private void StartRecording()
        {
            string outputFilePath = nomVideo;
            // Set up the VideoFileWriter instance
            Debug.WriteLine(pictureBox1.Width);
            Debug.WriteLine(pictureBox1.Height);

            videoFileWriter.Open(outputFilePath, 640, 480, framesPerSecond, VideoCodec.Default, 1000000);

            // Reset the recordedFrames counter
            recordedFrames = 0;
            videoRecording = true;
            videoFiniEnregistre = false;
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
            videoFiniEnregistre = true;
            videoRecording = false;
            // Close the VideoFileWriter instance
            videoFileWriter.Close();

            MessageBox.Show("Recording finished. The video file has been saved.", "Recording Finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            // Stop recording after reaching the desired number of frames
            if (recordedFrames == totalFramesToRecord && videoFiniEnregistre == false && videoRecording == true)
            {
                StopRecording();
                Task.Run(() =>
                {
                    sauvegardeVideoDansBD();
                });  
            }

            if (videoFiniEnregistre == false && videoRecording == true)
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

            Task.Run(() =>
            {
                conversionToBase64(image);
            });
                   
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
            byte[] videoBytes = GetVideoBytes(nomVideo);
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Execute a simple query
                var context = new MaisonConnecteEntities();

                // Create a new enregistrements object
                var newRecord = new enregistrement
                {
                    flux_video = videoBytes,
                    date = DateTime.Now,
                };

                // Add the new enregistrements object to the enregistrements DbSet
                context.enregistrement.Add(newRecord);

                // Save changes to the database
                context.SaveChanges();
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
