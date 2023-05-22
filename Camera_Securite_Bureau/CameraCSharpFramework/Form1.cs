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
using System.Net;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Exceptions;
using System.Linq;
using CameraCSharpFramework.Database;
/*----------------------------------------------------------------
 *  Auteur: Maxime Paulin
 *  
 *  Date de création: 22 Mars 2023à
 *  
 *  Dernière date de modification: [2023-05-21]
 *  
 *  Description: Ce programme est un système de caméra de sécurité conçus en C# et le protocole MQTT.
 *  
 *  Il est conçu pour surveiller, contrôler et enregistrer le flux vidéo de différentes caméras connectées
 *  dans un cadre de sécurité domestique. Il se connecte à un courtier MQTT (mosquito), en s'abonnant à des sujets liés à
 *  un capteur à ultrasons, à la commande d'éclairage et aux changements de couleur de la bande lumineuse. Le programme gère également les connexions client,
 *  diffusant des données vidéo sur des sockets aux clients connectés. Il fournit une interface utilisateur pour la sélection de la caméra
 *  et gère également l'enregistrement des flux vidéo lors de la détection d'un intrue et les sauvegarde dans une base de données.
 *----------------------------------------------------------------*/

/*----------------------------------------------------------------
 * Sources:
 *  - Documentation officielle de Microsoft C#: https://docs.microsoft.com/fr-fr/dotnet/csharp/
 *  - Documentation de la bibliothèque MQTTNet: https://github.com/chkr1011/MQTTnet
 *  - Communauté Stack Overflow: https://stackoverflow.com/
 *  - Accord pour le flux vidéo: http://accord-framework.net/
 *  - Entity framework : https://learn.microsoft.com/en-us/ef/
 *  - Sockets : https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets?view=netframework-4.8
 *  - LINQ C#: https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/
 *  - Chat GPTv4
 *----------------------------------------------------------------*/


namespace CameraCSharpFramework
{
    public partial class camera_securite_bureau : Form
    {
        // variables pour connecter à MQTT et au topic
        public static string nomServeur = "test.mosquitto.org";
        public static int portServeur = 1883;
        private static IMqttClient _clientMqtt;
        private static string SujetCapteurUltrason = "capteur_ultrason";
        private static string SujetLumiere = "allumer_led_divertissement";
        private static string SujetCouleur = "couleur_led_divertissement";
        //------------------------------------------------------
        private Bitmap image;
        private Bitmap imageRedimensionnee;
        //variable pour dire aux thread du socket darreter de fonctionner
        private CancellationTokenSource sourceAnnulationDiffusion;
        //Trouve les cameras sur l'ordinateur
        private FilterInfoCollection peripheriquesVideo;
        //driver directShow
        private VideoCaptureDevice peripheriqueCaptureVideo;
        //Données à envoyer au socket
        private static string imageBase64 = string.Empty;
        //Connection du client
        private static List<ClientConnection> clients = new List<ClientConnection>();
        
        //Calcule le nombre d'image a enregistré
        private static int imagesEnregistrees = 0;
        private const int imagesParSeconde = 30;
        private const int dureeVideoSouhaiteeSecondes = 1 * 10; // 5 minutes in seconds
        private const int totalFramesAEnregistrer = imagesParSeconde * dureeVideoSouhaiteeSecondes;
        private static int largeurPictureBox = 0;
        private static int hauteurPictureBox = 0;

        private static byte[] miniatureSiteWeb = null;
        private static VideoFileWriter ecriveurFichierVideo;
        private static string nomVideo = "temp.mp4";
        //Quand reçoit un message MQTT, mettre à true
        private static bool enregistrementVideo = false;
        private static string messageRecu;

        private static readonly object _verrouSynchro = new object();

        public camera_securite_bureau()
        {
            InitializeComponent();
        }

        //souscrit au topic et va lire les valeurs. Commence l'enregistrement lorsque porte ouverte.
        public static async Task Souscrire_Topic()
        {
            try
            {
                var fabriqueMqtt = new MqttFactory();
                _clientMqtt = fabriqueMqtt.CreateMqttClient();

                var optionsClientMqtt = new MqttClientOptionsBuilder().WithTcpServer(nomServeur, 1883).Build();

                _clientMqtt.ApplicationMessageReceivedAsync += (e) =>
                {
                    if (e.ApplicationMessage.Topic == SujetCapteurUltrason)
                    {
                        // Handle ultrasonic sensor message
                        messageRecu = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                        Debug.WriteLine($"Message reçu du topic '{e.ApplicationMessage.Topic}': {messageRecu}");
                    }
                    else if (e.ApplicationMessage.Topic == SujetLumiere)
                    {
                        // exécute une requete simple
                        var context = new maisonConnecteEntities();

                        // création d'un objet enregistrement
                        var newRecord = new @event
                        {
                            event1 = Encoding.UTF8.GetString(e.ApplicationMessage.Payload) == "1" ? EventEnum.LightOn : EventEnum.LightOff ,
                            date = DateTime.Now
                        };

                        // ajoute objet enregistrement au data set
                        context.events.Add(newRecord);

                        // sauvegarde l'objet enregistrement dans la base de donnée
                        context.SaveChanges();
                    }
                    else if (e.ApplicationMessage.Topic == SujetCouleur)
                    {
                        // exécute une requete simple
                        var context = new maisonConnecteEntities();

                        // création d'un objet enregistrement
                        var newRecord = new @event
                        {
                            event1 = EventEnum.LEDColor,
                            date = DateTime.Now
                        };

                        // ajoute objet enregistrement au data set
                        context.events.Add(newRecord);

                        // sauvegarde l'objet enregistrement dans la base de donnée
                        context.SaveChanges();
                    }
                    return Task.CompletedTask;
                };

                await _clientMqtt.ConnectAsync(optionsClientMqtt, CancellationToken.None);

                var mqttSubscribeOptions = fabriqueMqtt.CreateSubscribeOptionsBuilder()
                     .WithTopicFilter(
                    f =>
                    {
                        f.WithTopic(SujetCapteurUltrason);
                    })
                .WithTopicFilter(
                    f =>
                    {
                        f.WithTopic(SujetLumiere);
                    })
                .WithTopicFilter(
                    f =>
                    {
                        f.WithTopic(SujetCouleur);
                    })
                    .Build();

                var response = await _clientMqtt.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);

                Debug.WriteLine("MQTT client souscrit au sujet.");
            }
            catch (MqttCommunicationException ex)
            {
                Debug.WriteLine("une erreur est survenue lors de la communication au courtier: " + ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("une erreur s'est produite: " + ex.Message);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            largeurPictureBox = pictureBox1.Width;
            hauteurPictureBox = pictureBox1.Height;
            peripheriquesVideo = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            foreach (FilterInfo filterInfo in peripheriquesVideo)
            {
                comboBox1.Items.Add(filterInfo.Name);
            }
            comboBox1.SelectedIndex = 0;

            peripheriqueCaptureVideo = new VideoCaptureDevice();

            CreerSocket();

            peripheriqueCaptureVideo = new VideoCaptureDevice(peripheriquesVideo[0].MonikerString);
            peripheriqueCaptureVideo.NewFrame += new NewFrameEventHandler(PeripheriqueCaptureVideo_NouvelleImage);
            // met la resolution a 1280x720
            peripheriqueCaptureVideo.VideoResolution = peripheriqueCaptureVideo.VideoCapabilities
       .FirstOrDefault(capability => capability.FrameSize.Equals(new Size(1280, 720)));

            peripheriqueCaptureVideo.Start();
            // Initialize VideoFileWriter
            ecriveurFichierVideo = new VideoFileWriter();

            Task.Run(async () =>
            {
                await Souscrire_Topic();
            });
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (peripheriqueCaptureVideo != null && peripheriqueCaptureVideo.IsRunning)
            {
                peripheriqueCaptureVideo.SignalToStop();
                peripheriqueCaptureVideo.WaitForStop();
                peripheriqueCaptureVideo = null;
            }

            if (ecriveurFichierVideo != null)
            {
                ecriveurFichierVideo.Close();
                ecriveurFichierVideo.Dispose();
            }

            ArreterTacheDiffusion();
        }

        private static void DemarrerEnregistrement()
        {
            // Réinitialise le compteur de frames enregistrées
            imagesEnregistrees = 0;
            enregistrementVideo = true;
            string cheminFichierSortie = nomVideo;
            sauvegardeEvenementDansBD();

            ecriveurFichierVideo.Open(cheminFichierSortie, 1280, 720, imagesParSeconde, VideoCodec.Default, 1000000);      
        }

        private void ArreterTacheDiffusion()
        {
            if (sourceAnnulationDiffusion != null)
            {
                sourceAnnulationDiffusion.Cancel();
                sourceAnnulationDiffusion.Dispose();
                sourceAnnulationDiffusion = null;
            }
        }
        public static void Diffuser()
        {
            // Ajouter le délimiteur "---END_OF_FRAME---" à imageBase64
            string imageBase64AvecDelimiteur = imageBase64 + "---END_OF_FRAME---";

            byte[] octetsMessage = Encoding.ASCII.GetBytes(imageBase64AvecDelimiteur);
            ClientConnection connexion;

            lock (_verrouSynchro)
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    if (i >= clients.Count)
                    {
                        continue;
                    }
                    connexion = clients[i];
                    try
                    {
                        connexion.ClientSocket.Send(octetsMessage);
                    }
                    catch (SocketException)
                    {
                        GererConnexionClient(connexion);
                    }
                }
            }
        }

        private static void GererConnexionClient(ClientConnection connexion)
        {
            Debug.WriteLine($"Connexion client: {connexion}");

            // Nettoyage
            Debug.WriteLine("client supprimer");
            connexion.ClientSocket.Close();
            lock (_verrouSynchro)
            {
                clients.Remove(connexion);
            }
        }

        private void CreerSocket()
        {
            Debug.WriteLine("Socket créé");
            sourceAnnulationDiffusion = new CancellationTokenSource();
            var jeton = sourceAnnulationDiffusion.Token;

            Task.Run(() =>
            {
                int port = 8010;
                IPAddress adresseIP = IPAddress.Any;

                Socket serveurSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint pointFinLocal = new IPEndPoint(adresseIP, port);
                serveurSocket.Bind(pointFinLocal);
                serveurSocket.Listen(10);

                Debug.WriteLine($"Serveur écoute : {adresseIP}:{port}");

                while (true)
                {
                    Socket clientSocket = serveurSocket.Accept();
                    Debug.WriteLine("Client connected.");

                    ClientConnection connexionClient = new ClientConnection { ClientSocket = clientSocket };

                    lock (_verrouSynchro)
                    {
                        clients.Add(connexionClient);
                    }
                }
            });

            Task.Run(async () =>
            {
                while (!jeton.IsCancellationRequested)
                {
                    Diffuser();
                    await Task.Delay(1000 / 30);
                }
            });
        }
        private void button1_Click(object sender, EventArgs e)
        {
            if (peripheriqueCaptureVideo != null && peripheriqueCaptureVideo.IsRunning)
            {
                peripheriqueCaptureVideo.SignalToStop();
                peripheriqueCaptureVideo.WaitForStop();
                peripheriqueCaptureVideo = null;
            }
            peripheriqueCaptureVideo = new VideoCaptureDevice(peripheriquesVideo[comboBox1.SelectedIndex].MonikerString);
            peripheriqueCaptureVideo.NewFrame += new NewFrameEventHandler(PeripheriqueCaptureVideo_NouvelleImage);
            peripheriqueCaptureVideo.Start();
        }
        private void ArreterEnregistrement()
        {
            enregistrementVideo = false;
            messageRecu = "";
            // Ferme l'instance de VideoFileWriter
            ecriveurFichierVideo.Close();
        }

        //Généré par chat gpt: Permet de prendre la vidéo et de prendre les octets pour les envoyer plus tard dans le blob dans la basse de données
        static byte[] ObtenirOctetsVideo(string cheminFichierVideo)
        {
            using (FileStream fluxFichier = new FileStream(cheminFichierVideo, FileMode.Open, FileAccess.Read))
            {
                using (MemoryStream fluxMemoire = new MemoryStream())
                {
                    fluxFichier.CopyTo(fluxMemoire);
                    return fluxMemoire.ToArray();
                }
            }
        }

        private void PeripheriqueCaptureVideo_NouvelleImage(object sender, NewFrameEventArgs eventArgs)
        {
            image = (Bitmap)eventArgs.Frame.Clone();
            

            if (enregistrementVideo == false && messageRecu == "1")
            {
                DemarrerEnregistrement();
            }

            // Arrête l'enregistrement après avoir atteint le nombre d'images souhaité
            if (imagesEnregistrees == totalFramesAEnregistrer && enregistrementVideo == true)
            {
                ArreterEnregistrement();
                sauvegardeVideoDansBD();
            }
            // rendu à moitié on enregistre l'aperçu et on envoie dans la BD
            else if (imagesEnregistrees == (totalFramesAEnregistrer/2))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    //sauvegarde l'image dans le buffer
                    image.Save(ms, ImageFormat.Jpeg);

                    // convertit le memory stream dans un tableau d'octets
                    miniatureSiteWeb = ms.ToArray();
                }
            }

            if (enregistrementVideo == true)
            {
                imagesEnregistrees++;
                // Sauvegarde des images dans l'instance de VideoFileWriter pendant la capture
              
                ecriveurFichierVideo.WriteVideoFrame(image);
            }

            imageRedimensionnee = new Bitmap(largeurPictureBox, hauteurPictureBox);

            //généré par chat gpt: Permet de "rezise" l'image trop grande pour que sa entre dans la picture box
            using (Graphics graphics = Graphics.FromImage(imageRedimensionnee))
            {
                graphics.DrawImage(image, 0, 0, largeurPictureBox, hauteurPictureBox);
            }
            
            ConversionEnBase64(image);
                
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = imageRedimensionnee;
          
        }
        private void ConversionEnBase64(Bitmap image)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Jpeg);

                byte[] imageBytes = ms.ToArray();

                string base64string = Convert.ToBase64String(imageBytes);
                byte[] utf8bytes = System.Text.Encoding.UTF8.GetBytes(base64string);

                imageBase64 = System.Text.Encoding.ASCII.GetString(utf8bytes);
            }
        }

        private static void sauvegardeEvenementDansBD()
        {
            // exécute une requete simple
            var context = new maisonConnecteEntities();

            // création d'un objet enregistrement
            var newRecord = new @event
            {
                event1 = EventEnum.DoorStatusChanged,
                date = DateTime.Now
            };

            // ajoute objet enregistrement au data set
            context.events.Add(newRecord);

            // sauvegarde l'objet enregistrement dans la base de donnée
            context.SaveChanges();
        }

        private void sauvegardeVideoDansBD()
        {                                    
            byte[] video = ObtenirOctetsVideo(nomVideo);

            // exécute une requete simple
            var context = new maisonConnecteEntities();

            // création d'un objet enregistrement
            var newRecord = new enregistrement
            {
                flux_video = video,
                thumbnail = miniatureSiteWeb,
                date = DateTime.Now,
            };

            // ajoute objet enregistrement au data set
            context.enregistrements.Add(newRecord);

            // sauvegarde l'objet enregistrement dans la base de donnée
            context.SaveChanges();               
                 
            try
                {
                    File.Delete(nomVideo);
                    Debug.WriteLine("fichier supprimer avec succès.");
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"erreur lors de la supprimation du fichier: {ex.Message}");
                }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // raffraichie les péripherique vidéos
            peripheriquesVideo = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            // nettois le combo box
            comboBox1.Items.Clear();

            // ajoute les nouveaux objets au combo box
            foreach (FilterInfo filterInfo in peripheriquesVideo)
            {
                comboBox1.Items.Add(filterInfo.Name);
            }

            // met automatiquement le premier objet à l'index
            if (comboBox1.Items.Count > 0)
            {
                comboBox1.SelectedIndex = 0;
            }
        }
    }
}