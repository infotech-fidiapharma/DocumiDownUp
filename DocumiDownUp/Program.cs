/*
#

# Copyright © Fidia Farmaceutici s.p.a. - All Rights Reserved
#
# Unauthorized copying of this file, via any medium is strictly prohibited
# Proprietary and confidential
#
# Written by Piero Giacomelli <pgiacomelli@fidiapharma.it>, Jul 2018
#
#
#---------------------------------------------------------------------*
# Autore:      Piero Giacomelli
# Data:        2018-10-29
# Descrizione: console application per la gestione dello scarico fatture da Documi
# 
#---------------------------------------------------------------------*
*/

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Xsl;
using Fonet;
using Renci.SshNet.Sftp;

namespace DocumiDownUp
{
    class Program
    {
        private static Renci.SshNet.ConnectionInfo connectioninfo = null;
        private static string SFTP_HOST = string.Empty;
        private static string SFTP_USER = string.Empty;
        private static string SFTP_PWD = string.Empty;
        private static string SFTP_DOWNLOAD_FOLDER = string.Empty;
        private static string SFTP_UPLOAD_FOLDER = string.Empty;

        private static string LOCAL_DOWNLOAD_FOLDER = string.Empty;
        private static string LOCAL_UPLOAD_FOLDER = string.Empty;
        private static string LOCAL_UPLOAD_FOLDER_BACKUP = string.Empty;
        private static string LOCAL_FILES_FOLDER = string.Empty;
        private static string LOCAL_PDF_FOLDER = string.Empty;
        private static bool YES_TO_DOWNLOAD = false;
        private static bool YES_TO_UPLOAD = true;



        private static Renci.SshNet.SftpClient SFTPClient;


        static void Main(string[] args)
        {
            InitSettings();
            ConnectDocumi();
            DownloadFiles();
            UploadFiles();
            DisconnectDocumi();
        }

        private static void DisconnectDocumi()
        {
            if (IsActiveConnection())
            {
                Utils.CLogger.WriteLog(" disconnecting from " + SFTP_HOST);
                SFTPClient.Disconnect();
                SFTPClient.Dispose();
                SFTPClient = null;
                Utils.CLogger.WriteLog(" EXITING ");
                Environment.Exit(0);
            }
        }

        private static void UploadFiles()
        {
            string destination_full_name = "";
            if (!YES_TO_UPLOAD) return;

            try
            {
                if (IsActiveConnection())
                {
                    DirectoryInfo d = new DirectoryInfo(LOCAL_UPLOAD_FOLDER);
                    FileInfo[] Files = d.GetFiles("*.xml");

                    foreach (FileInfo file in Files)
                    {
                        Utils.CLogger.WriteLog("start uploading file " + file.FullName);
                        try
                        {
                            using (var fileStream = new FileStream(file.FullName, FileMode.Open))
                            {
                                Utils.CLogger.WriteLog("Uploading " + file.FullName);
                                SFTPClient.BufferSize = 4 * 1024; // bypass Payload error large files
                                SFTPClient.UploadFile(fileStream, file.FullName);
                                Utils.CLogger.WriteLog(file.FullName + "Uploaded");

                                Utils.CLogger.WriteLog("moving  " + file.FullName + " to backup folder" + LOCAL_UPLOAD_FOLDER_BACKUP);
                                destination_full_name = Path.Combine(LOCAL_UPLOAD_FOLDER_BACKUP, file.Name);
                                System.IO.File.Move(file.FullName, destination_full_name);
                                Utils.CLogger.WriteLog("file " + file.FullName + " moved ");


                            }

                            Utils.CLogger.WriteLog("end uploading file " + file.FullName);
                        }
                        catch (Exception ex)
                        {
                            Utils.CLogger.WriteLog(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }

        }

        private static bool IsActiveConnection()
        {
            return ((connectioninfo.IsAuthenticated) && (SFTPClient.IsConnected));
        }

        private static void DownloadFiles()
        {
            if (!YES_TO_DOWNLOAD) return;

            try
            {
                if (IsActiveConnection())
                {
                    MoveToDownloadFolder();
                    DownloadAllNewSuppliersInvocesFiles();
                    ParseAllNewSupplierInvocesFiles();
                }
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }

        }

        private static void ParseAllNewSupplierInvocesFiles()
        {
            try
            {
                DirectoryInfo d = new DirectoryInfo(LOCAL_DOWNLOAD_FOLDER);//Assuming Test is your Folder
                FileInfo[] Files = d.GetFiles("*.xml"); //Getting Text files                
                foreach (FileInfo file in Files)
                {
                    Utils.CLogger.WriteLog("start parsing file " + file.FullName);
                    ParseXMLSupplierInvoice(file.FullName, file.Name);
                    CreatePDFFromXML(file.FullName, file.Name);
                    Utils.CLogger.WriteLog("end parsing file " + file.FullName);
                }
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }

        }

        private static void CreatePDFFromXML(string fullNameXml, string shortname)
        {
            try
            {
                string pXslFile = @".\config\FoglioStileAssoSoftware.xsl";
                string pFoFile = Path.Combine(LOCAL_PDF_FOLDER, shortname.Replace("xml", "html"));
                string pPdfFile = Path.Combine(LOCAL_PDF_FOLDER, shortname.Replace("xml", "pdf"));
                string lBaseDir = System.IO.Path.GetDirectoryName(pPdfFile);
                string xsltString = File.ReadAllText(pXslFile);

                //XslCompiledTransform lXslt = new XslCompiledTransform();
                //lXslt.Load(pXslFile);
                //lXslt.Transform(fullNameXml, pFoFile);
                //FileStream lFileInputStreamFo = new FileStream(pFoFile, FileMode.Open);
                //FileStream lFileOutputStreamPDF = new FileStream(pPdfFile, FileMode.Create);
                //FonetDriver lDriver = FonetDriver.Make();
                //lDriver.BaseDirectory = new DirectoryInfo(lBaseDir);
                //lDriver.CloseOnExit = true;
                //lDriver.Render(lFileInputStreamFo, lFileOutputStreamPDF);
                //lFileInputStreamFo.Close();
                //lFileOutputStreamPDF.Close();
                //if (System.IO.File.Exists(pFoFile))
                //    System.IO.File.Delete(pFoFile);

                XslCompiledTransform proc = new XslCompiledTransform();

                proc.Load(pXslFile);

                string result;

                using (StringWriter sw = new StringWriter())

                {

                    proc.Transform(fullNameXml, null, sw);

                    result = sw.ToString();

                    var Renderer = new IronPdf.HtmlToPdf();
                    var PDF = Renderer.RenderHtmlAsPdf(result);
                    var OutputPath = pPdfFile;

                    if (System.IO.File.Exists(OutputPath))
                        System.IO.File.Delete(OutputPath);

                    PDF.SaveAs(OutputPath);


                }


            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

        }

        private static void ParseXMLSupplierInvoice(string fullName, string shortname)
        {
            string nome_file = string.Empty;
            string estensione = string.Empty;
            string new_files_fullname = string.Empty;


            try
            {
                using (XmlReader reader = XmlReader.Create(fullName))
                {
                    while (reader.Read())
                    {
                        if (reader.IsStartElement() && reader.NodeType == XmlNodeType.Element)
                        {

                            if (reader.Name == "NomeAttachment")
                            {
                                Utils.CLogger.WriteLog("find attachmente inside  " + fullName + " finding extension...");
                                nome_file = reader.ReadInnerXml();
                                estensione = GetExtensionByName(nome_file);
                                new_files_fullname = Path.Combine(LOCAL_FILES_FOLDER, shortname.Replace("xml", estensione));
                                Utils.CLogger.WriteLog("find attachmente inside  " + fullName + " with extension " + estensione);
                            }

                            if (reader.Name == "FormatoAttachment")
                            {
                                estensione = reader.ReadInnerXml().Replace(".", "");
                                new_files_fullname = Path.Combine(LOCAL_FILES_FOLDER, shortname.Replace("xml", estensione));
                            }

                            if (reader.Name == "Attachment")
                            {
                                Utils.CLogger.WriteLog("saving attachmente with extension " + estensione + " to " + new_files_fullname);

                                if (System.IO.File.Exists(new_files_fullname))
                                {
                                    Utils.CLogger.WriteLog(" file " + new_files_fullname + " already existing deleting it");
                                    System.IO.File.Delete(new_files_fullname);
                                    Utils.CLogger.WriteLog(" file " + new_files_fullname + " DELETED !!!");
                                }


                                var xmlStringValue = reader.ReadInnerXml();

                                var buffer = Convert.FromBase64String(xmlStringValue);
                                File.WriteAllBytes(new_files_fullname, buffer);

                                Utils.CLogger.WriteLog("file  " + new_files_fullname + " SAVED !!!!");

                                if ("pdf".Equals(estensione).ToString().ToLower())
                                {
                                    ExtractTextFromPDF(new_files_fullname, shortname);
                                }

                                break;
                            }


                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }

        }

        private static void ExtractTextFromPDF(string new_files_fullname, string shortname)
        {
            throw new NotImplementedException();
        }

        private static string GetExtensionByName(string nome_file)
        {
            try
            {
                if (nome_file.LastIndexOf(".") > 0)
                    return nome_file.Substring(nome_file.LastIndexOf(".") + 1);
                else
                    return "pdf";
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
                return "pdf";
            }
        }

        private static void DownloadAllNewSuppliersInvocesFiles()
        {
            try
            {
                var files = SFTPClient.ListDirectory(SFTP_DOWNLOAD_FOLDER);
                foreach (var file in files)
                {
                    Utils.CLogger.WriteLog("analysing " + file.FullName);
                    if (!file.IsDirectory && !file.IsSymbolicLink)
                    {
                        DownloadSingleInvoice(file);
                    }
                    else if (file.IsSymbolicLink)
                    {
                        Utils.CLogger.WriteLog("Ignoring symbolic link " + file.FullName);
                    }
                    else if (file.Name != "." && file.Name != "..")
                    {
                        /*                        var dir = Directory.CreateDirectory(Path.Combine(destination, file.Name));
                                                DownloadDirectory(client, file.FullName, dir.FullName);                        
                        */
                    }

                }

            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void DownloadSingleInvoice(SftpFile file)
        {
            string FullLocalPath = Path.Combine(LOCAL_DOWNLOAD_FOLDER, file.Name);
            try
            {
                if (System.IO.File.Exists(FullLocalPath))
                {
                    Utils.CLogger.WriteLog("file " + file.FullName + " already present in destination folder removing it before downloading ");
                    System.IO.File.Delete(FullLocalPath);
                    Utils.CLogger.WriteLog("file " + file.FullName + " DELETED !!!! ");
                }
                using (Stream fileStream = File.OpenWrite(FullLocalPath))
                {
                    Utils.CLogger.WriteLog("start donwloading " + file.FullName);
                    SFTPClient.DownloadFile(file.FullName, fileStream);
                    Utils.CLogger.WriteLog(file.FullName + " downloaded");
                }
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void MoveToDownloadFolder()
        {
            SendSSHCommand(SFTP_DOWNLOAD_FOLDER);
        }

        private static void SendSSHCommand(string command)
        {
            try
            {
                Utils.CLogger.WriteLog("try to send command ");
                Utils.CLogger.WriteLog(command);
                Utils.CLogger.WriteLog("command sent ");
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void ConnectDocumi()
        {
            try
            {
                if (connectioninfo == null)
                {
                    ReadConnectionInfoParameters();
                    OpenSftpConnection();
                }
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void OpenSftpConnection()
        {
            try
            {
                Utils.CLogger.WriteLog("Opening SFTP CONNECTION START");

                connectioninfo = new Renci.SshNet.ConnectionInfo(SFTP_HOST, SFTP_USER, new Renci.SshNet.PasswordAuthenticationMethod(SFTP_USER, SFTP_PWD), new Renci.SshNet.PrivateKeyAuthenticationMethod("rsa.key"));
                SFTPClient = new Renci.SshNet.SftpClient(connectioninfo);
                SFTPClient.Connect();

                Utils.CLogger.WriteLog("SFTP CONNECTION Opened with HOST " + SFTP_HOST);
                Utils.CLogger.WriteLog("Waiting for commands ");
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }

        }

        private static void ReadConnectionInfoParameters()
        {
            try
            {
                SFTP_HOST = ConfigurationManager.AppSettings["SFTP_HOST"];
                SFTP_USER = ConfigurationManager.AppSettings["SFTP_USER"];
                SFTP_PWD = ConfigurationManager.AppSettings["SFTP_PWD"];
                SFTP_DOWNLOAD_FOLDER = ConfigurationManager.AppSettings["SFTP_DOWNLOAD_FOLDER"];
                LOCAL_DOWNLOAD_FOLDER = ConfigurationManager.AppSettings["LOCAL_DOWNLOAD_FOLDER"];
                LOCAL_FILES_FOLDER = ConfigurationManager.AppSettings["LOCAL_FILES_FOLDER"];
                SFTP_UPLOAD_FOLDER = ConfigurationManager.AppSettings["SFTP_UPLOAD_FOLDER"];
                LOCAL_UPLOAD_FOLDER = ConfigurationManager.AppSettings["LOCAL_UPLOAD_FOLDER"];
                LOCAL_PDF_FOLDER = ConfigurationManager.AppSettings["LOCAL_PDF_FOLDER"];
                YES_TO_DOWNLOAD = Convert.ToBoolean(ConfigurationManager.AppSettings["YES_TO_DOWNLOAD"].ToString());
                YES_TO_UPLOAD = Convert.ToBoolean(ConfigurationManager.AppSettings["YES_TO_UPLOAD"].ToString());         

                Utils.CLogger.WriteLog("reading connection info details from config file START");
                Utils.CLogger.WriteLog(SFTP_HOST);
                Utils.CLogger.WriteLog(SFTP_USER);
                Utils.CLogger.WriteLog(SFTP_PWD);
                Utils.CLogger.WriteLog(SFTP_DOWNLOAD_FOLDER);
                Utils.CLogger.WriteLog(SFTP_UPLOAD_FOLDER);
                Utils.CLogger.WriteLog(LOCAL_DOWNLOAD_FOLDER);
                Utils.CLogger.WriteLog(LOCAL_FILES_FOLDER);
                Utils.CLogger.WriteLog(LOCAL_PDF_FOLDER);

                Utils.CLogger.WriteLog(YES_TO_DOWNLOAD);
                Utils.CLogger.WriteLog(YES_TO_UPLOAD);

                Utils.CLogger.WriteLog("reading connection info details from config file END");
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void InitSettings()
        {
            InitLogging();
        }

        private static void InitLogging()
        {
            Utils.CLogger.configuration_file_path = ConfigurationManager.AppSettings["log4net_configfile"];
            Utils.CLogger.ConfigureLoggger();
            Utils.CLogger.LogSystemProperties();
        }
    }
}
