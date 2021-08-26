/*
#

# Copyright © Fidia Farmaceutici s.p.a. - All Rights Reserved
#
# Unauthorized copying of this file, via any medium is strictly prohibited
# Proprietary and confidential
#
# Written by Piero Giacomelli <pgiacomelli@fidiapharma.it>, Nov 2018
#
#
#---------------------------------------------------------------------*
# Autore:      Piero Giacomelli
# Data:        2018-10-29
# Descrizione: console application per la gestione dello scarico fatture da Documi
# Data:        2021-04-26
# Descrizione: aggiunta gestione del rinomina se il file ha lo stesso nome (MAIUSCOLE / MINUSCOLE)
# 
#---------------------------------------------------------------------*
*/

using System;
using System.Configuration;
using System.IO;
using System.Net.Mail;
using System.Xml;
using System.Xml.Xsl;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
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
        private static string LOCAL_TXT_FOLDER = string.Empty;



        private static bool YES_TO_DOWNLOAD = true;
        private static bool YES_TO_UPLOAD = false;

        private static bool DELETE_XML_AFTER_DONWLOAD = true;
        private static bool EXTRACT_PDF_BEFORE_UPLOAD = true;

        private static Renci.SshNet.SftpClient SFTPClient;

        public static string SFTP_UPLOAD_FOLDER_CONSERVAZIONE { get; private set; }
        public static string LOCAL_UPLOAD_FOLDER_CONSERVAZIONE { get; private set; }
        public static string LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP { get; private set; }

        public static string  LOCAL_DOWNLOAD_FOLDER_ESITI = string.Empty;
        public static string LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP = string.Empty;
        public static string SFTP_DOWNLOAD_FOLDER_ESITI = string.Empty;

        private static bool CHECK_ESITI = true;
        private static bool  REMOVE_ESITI_ONCE_DOWNLOAD = true;
        private static string EMAIL_ESITI = "";



        private static bool YES_TO_UPLOAD_CONSERVAZIONE = false;
        private static bool MOVE_ESITI_ONCE_DOWNLOAD = false;
        



        static void Main(string[] args)
        {
            
            InitSettings();
            ReadConnectionInfoParameters(args);
            ConnectDocumi();
            CreateFolders();
#region fatturazione        
            DownloadFiles();
            UploadFiles();
            #endregion

            #region conservazione  
            RenameAndUploadFilesForConservazione();

            #endregion

            #region esiti
            //DownloadFilesEsiti();
            CheckFilesEsiti();
            #endregion esiti
            DisconnectDocumi();
        }

        private static void CheckFilesEsiti()
        {
            try
            {
                if (!CHECK_ESITI) return;

                MoveToDownloadFolder(SFTP_DOWNLOAD_FOLDER_ESITI);

                var files = SFTPClient.ListDirectory(SFTP_DOWNLOAD_FOLDER_ESITI);
                foreach (var file in files)
                {                    
                    Utils.CLogger.WriteLog("analysing " + file.FullName);
                    if (!file.IsDirectory && !file.IsSymbolicLink)
                    {
                        DownloadSingleFile(file, LOCAL_DOWNLOAD_FOLDER_ESITI); //TODO aggiungi la rimozione 

                        if (DELETE_XML_AFTER_DONWLOAD)
                        {
                            DeleteXmlAfterDownload(file.FullName);
                        }

                        AnalyseSingleFileEsiti(file, LOCAL_DOWNLOAD_FOLDER_ESITI);

                        if (MOVE_ESITI_ONCE_DOWNLOAD)
                            MoveEsitiToDownloadFolderBackup(file.Name);                            

                    }
                    else if (file.IsSymbolicLink)
                    {
                        Utils.CLogger.WriteLog("Ignoring symbolic link " + file.FullName);
                    }
                    else if (file.Name != "." && file.Name != "..")
                    {
                    }

                }                
                
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("errore nel metodo  CheckFilesEsiti");
                Utils.CLogger.WriteLog(ex);

            }
        }

        private static void MoveEsitiToDownloadFolderBackup(string pname)
        {
            try
            {
                string fullsourcename = System.IO.Path.Combine(LOCAL_DOWNLOAD_FOLDER_ESITI, pname); ;
                string fulldestinationname = System.IO.Path.Combine(LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP, pname); ;

                if (System.IO.File.Exists(fulldestinationname))
                    System.IO.File.Delete(fulldestinationname);
                Utils.CLogger.WriteLog("moving " + fullsourcename + " to " + fulldestinationname);
                System.IO.File.Move(fullsourcename, fulldestinationname);
                Utils.CLogger.WriteLog(fullsourcename + " MOVED ");                
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("errore nel metodo  MoveEsitiToDownloadFolderBackup");
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void AnalyseSingleFileEsiti(SftpFile file, string plOCAL_DOWNLOAD_FOLDER_ESITI)
        {
            bool ThereIsAnError = false;
            try
            {
                string FullLocalPathName = System.IO.Path.Combine(plOCAL_DOWNLOAD_FOLDER_ESITI, file.Name);
                string Codice = "";
                string Descrizione = "";
                string Suggerimento = "";


                //cerco il seguente tag

                ////< ListaErrori >
                ////    < Errore >
                ////        < Codice > 00311 </ Codice >
                ////        < Descrizione > 1.1.4 CodiceDestinatario non valido: Codice Destinatario B2B KRRM6B9 non trovato</ Descrizione >

                ////          < Suggerimento > Verificare il CodiceDestinatario: potrebbe non essere corretto, non essere presente su IPA o non essere ancora abilitato alla ricezione del file FatturaPA</ Suggerimento >

                ////         </ Errore >

                ////     </ ListaErrori >
                ///
                using (XmlReader reader = XmlReader.Create(FullLocalPathName))
                {
                    while (reader.Read())
                    {
                        if (reader.IsStartElement() && reader.NodeType == XmlNodeType.Element)
                        {

                            if (reader.Name == "ListaErrori")
                            {
                                ThereIsAnError = true;
                            }

                            if (ThereIsAnError)
                            {
                                if (reader.Name == "Codice") Codice = reader.ReadInnerXml();
                                if (reader.Name == "Descrizione") Descrizione = reader.ReadInnerXml();
                                if (reader.Name == "Suggerimento") Suggerimento = reader.ReadInnerXml();
                            }
                        }
                    }
                }

                if (Codice != "" && Descrizione != "" && Suggerimento != "")
                {
                    string body = "";
                    body += Codice + "<br>";
                    body += Descrizione + "<br>";
                    body += Suggerimento + "<br>";
                    SendMailMessage(body);
                }

            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("errore nel metodo  CheckFilesEsiti");
                Utils.CLogger.WriteLog(ex);

            }

        }

        private static void SendMailMessage(string body)
        {

            //try
            //{

            //    MailMessage mail = new MailMessage("notifications@fidiapharma.it", EMAIL_ESITI);
            //    SmtpClient client = new SmtpClient();
            //    client.Port = 25;
            //    client.DeliveryMethod = SmtpDeliveryMethod.Network;
            //    client.UseDefaultCredentials = false;
            //    client.Host = "owa.fidiapharma.it";
            //    mail.Subject = "errore file esiti";
            //    mail.Body = body;
            //    mail.IsBodyHtml = true;
            //    client.Send(mail);

            //}
            //catch (Exception ex)
            //{
            //    Utils.CLogger.WriteLog(ex);
            //}
        }

        private static void CreateFolderIfExists(string pfolder_full_path)
        {
            try
            {
                Utils.CLogger.WriteLog("checking if folder " + pfolder_full_path + " exists ");

                if (!System.IO.Directory.Exists(pfolder_full_path))
                {
                    Utils.CLogger.WriteLog("folder " + pfolder_full_path + " does not exists create it ");
                    System.IO.Directory.CreateDirectory(pfolder_full_path);
                }
                else
                {
                    Utils.CLogger.WriteLog("folder " + pfolder_full_path + " already exists nothing to do ");
                }

            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("error in method  public static void CreateFolderIfExists(string pfolder_full_path)");
                Utils.CLogger.WriteLog(ex.Message.ToString());

            }
        }

        private static string CheckSettingString(string p_setting_value)
        {
            try
            {
                Utils.CLogger.WriteLog("try to get string value for " + p_setting_value);
                return ConfigurationManager.AppSettings[p_setting_value].ToString();
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex.Message);
                Utils.CLogger.WriteLog("cannot find string setting for : " + p_setting_value + " forcing to false");
                return "";
            }
        }


        private static bool CheckSettingBool(string p_setting_value)
        {
            bool tmp = false;
            try
            {
                Utils.CLogger.WriteLog("trying to get boolean value for " + p_setting_value);
                tmp = Convert.ToBoolean(ConfigurationManager.AppSettings[p_setting_value].ToString());
                Utils.CLogger.WriteLog("findboolean value for " + p_setting_value);
                return tmp;
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("cannot find bool setting for : " + p_setting_value + " forcing to false");
                Utils.CLogger.WriteLog(ex.Message);
                return false;
            }
        }

        private static void DownloadFilesEsiti()
        {
            try
            {
                if (!CHECK_ESITI) return;

                MoveToDownloadFolder(SFTP_DOWNLOAD_FOLDER_ESITI);

                var files = SFTPClient.ListDirectory(SFTP_DOWNLOAD_FOLDER_ESITI);
                foreach (var file in files)
                {
                    Utils.CLogger.WriteLog("analysing " + file.FullName);
                    if (!file.IsDirectory && !file.IsSymbolicLink)
                    {
                        DownloadSingleFile(file,LOCAL_DOWNLOAD_FOLDER_ESITI);
                        if (REMOVE_ESITI_ONCE_DOWNLOAD)
                            DeleteXmlAfterDownload(file.FullName);
                    }
                    else if (file.IsSymbolicLink)
                    {
                        Utils.CLogger.WriteLog("Ignoring symbolic link " + file.FullName);
                    }
                    else if (file.Name != "." && file.Name != "..")
                    {
                    }

                }

            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("errore nel metodo  DownloadFilesEsiti " );
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void RenameAndUploadFilesForConservazione()
        {
            if (!YES_TO_UPLOAD_CONSERVAZIONE) return;


            Utils.CLogger.WriteLog("sending per conservazione inizio");

            DirectoryInfo d;
            FileInfo[] Files;

            try
            {
                if (SFTPClient.IsConnected)
                {
                    d = new DirectoryInfo(LOCAL_UPLOAD_FOLDER_CONSERVAZIONE);
                    Files = d.GetFiles("*.ctrl");

                    foreach (FileInfo file in Files)
                    {
                        try
                        {
                            Utils.CLogger.WriteLog("deleting: " + file.FullName);
                            System.IO.File.Delete(file.FullName);
                            Utils.CLogger.WriteLog(file.FullName + "deleted!!!");
                        }
                        catch (Exception ex)
                        {
                            Utils.CLogger.WriteLog(ex);
                        }

                    }

                    Files = d.GetFiles("*.tmp");
                    string xmlname = "";

                    foreach (FileInfo file in Files)
                    {
                        try
                        {
                            xmlname = file.FullName.Replace(".tmp", ".xml");
                            Utils.CLogger.WriteLog("renaming: " + file.FullName + " to " + xmlname);
                            if (System.IO.File.Exists(file.FullName))
                            { 
                                System.IO.File.Move(file.FullName, xmlname);
                            }
                            Utils.CLogger.WriteLog(file.FullName + "renamed!!!");
                        }
                        catch (Exception ex)
                        {
                            Utils.CLogger.WriteLog(ex);
                        }

                    }


                    SFTPClient.ChangeDirectory(SFTP_UPLOAD_FOLDER_CONSERVAZIONE);
                    SFTPClient.BufferSize = 4 * 1024; // bypass Payload error large files

                    Files = d.GetFiles();

                    //destination_full_name = System.IO.Path.Combine(LOCAL_UPLOAD_FOLDER_BACKUP, file.Name);

                    foreach (FileInfo file in Files)
                    {
                        try
                        {


                            Utils.CLogger.WriteLog("uploading: " + file.FullName + " to ");

                            using (var fileStream = new FileStream(file.FullName, FileMode.Open))
                            {
                                Utils.CLogger.WriteLog("Uploading " + file.FullName);
                                SFTPClient.UploadFile(fileStream, file.Name);
                                Utils.CLogger.WriteLog(file.FullName + "Uploaded");

                            }


                            Utils.CLogger.WriteLog(file.FullName + "uploaded !!!");

                            Utils.CLogger.WriteLog("moving " + file.FullName + " to backup folder");
                            System.IO.File.Move(file.FullName, System.IO.Path.Combine(LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP, file.Name));
                            Utils.CLogger.WriteLog(file.FullName + " copied");

                        }
                        catch (Exception ex)
                        {
                            Utils.CLogger.WriteLog(ex);
                        }

                    }


                    foreach (FileInfo file in Files)
                    {
                        try
                        {




                        }
                        catch (Exception ex)
                        {
                            Utils.CLogger.WriteLog(ex);
                        }

                    }



                }
                Utils.CLogger.WriteLog("sending per conservazione fine");
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
        }

        private static void CreateFolders()
        {
            try
            {
                if (YES_TO_DOWNLOAD)
                {
                    CreateFolderIfExists(LOCAL_DOWNLOAD_FOLDER);
                    CreateFolderIfExists(LOCAL_UPLOAD_FOLDER);
                    CreateFolderIfExists(LOCAL_UPLOAD_FOLDER_BACKUP);
                    CreateFolderIfExists(LOCAL_FILES_FOLDER);
                    CreateFolderIfExists(LOCAL_PDF_FOLDER);
                    CreateFolderIfExists(LOCAL_TXT_FOLDER);
                }

                if (CHECK_ESITI)
                {
                    CreateFolderIfExists(LOCAL_DOWNLOAD_FOLDER_ESITI);
                    CreateFolderIfExists(LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP);

                }

                if (YES_TO_UPLOAD_CONSERVAZIONE)
                {
                    CreateFolderIfExists(LOCAL_UPLOAD_FOLDER_CONSERVAZIONE);
                    CreateFolderIfExists(LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP);
                }





                /*        public static string SFTP_UPLOAD_FOLDER_CONSERVAZIONE { get; private set; }
                        public static string LOCAL_UPLOAD_FOLDER_CONSERVAZIONE { get; private set; }
                        public static string LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP { get; private set; }
                        */

            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
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
                    SFTPClient.BufferSize = 4 * 1024; // bypass Payload error large files

                    foreach (FileInfo file in Files)
                    {
                        Utils.CLogger.WriteLog("start uploading file " + file.FullName);
                        try
                        {
                            if (System.IO.File.Exists(file.FullName)) 
                            {
                                using (var fileStream = new FileStream(file.FullName, FileMode.Open))
                                {
                                    destination_full_name = SFTP_UPLOAD_FOLDER + file.Name;
                                    Utils.CLogger.WriteLog("Uploading " + file.FullName + " to " + destination_full_name);


                                    SFTPClient.UploadFile(fileStream, destination_full_name);
                                    Utils.CLogger.WriteLog(file.FullName + "Uploaded");
                                }

                                Utils.CLogger.WriteLog("moving  " + file.FullName + " to backup folder" + LOCAL_UPLOAD_FOLDER_BACKUP);
                                destination_full_name = System.IO.Path.Combine(LOCAL_UPLOAD_FOLDER_BACKUP, file.Name);
                                if (System.IO.File.Exists(destination_full_name))
                                {
                                    Utils.CLogger.WriteLog("file " + destination_full_name + " ALREADY EXISTS!!!! REMOVING ");
                                    System.IO.File.Delete(destination_full_name);
                                    Utils.CLogger.WriteLog("file " + destination_full_name + " REMOVED SUCCESSFULLY ");
                                }

                                System.IO.File.Move(file.FullName, destination_full_name); //TODO aggiungere la sovrascrittura altrimenti il programma va in errore
                                Utils.CLogger.WriteLog("file " + file.FullName + " moved ");


                                Utils.CLogger.WriteLog("end uploading file " + file.FullName);
                            }

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
                    MoveToDownloadFolder(SFTP_DOWNLOAD_FOLDER);
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
                string pFoFile = System.IO.Path.Combine(LOCAL_PDF_FOLDER, shortname.Replace("xml", "html"));
                string pPdfFile = System.IO.Path.Combine(LOCAL_PDF_FOLDER, shortname.Replace("xml", "pdf"));
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
                                new_files_fullname = System.IO.Path.Combine(LOCAL_FILES_FOLDER, shortname.Replace("xml", estensione));
                                Utils.CLogger.WriteLog("find attachmente inside  " + fullName + " with extension " + estensione);
                            }

                            if (reader.Name == "FormatoAttachment")
                            {
                                estensione = reader.ReadInnerXml().Replace(".", "");
                                new_files_fullname = System.IO.Path.Combine(LOCAL_FILES_FOLDER, shortname.Replace("xml", estensione));
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

                                if (estensione.ToLower() == "pdf")
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
            try
            {
                string txtpath = "";
                PdfReader reader = new PdfReader(new_files_fullname);

                StringWriter output = new StringWriter();

                for (int i = 1; i <= reader.NumberOfPages; i++)
                    output.WriteLine(PdfTextExtractor.GetTextFromPage(reader, i, new SimpleTextExtractionStrategy()));

                txtpath = System.IO.Path.Combine(LOCAL_TXT_FOLDER, shortname.Replace("xml", "txt"));

                if (System.IO.File.Exists(txtpath))
                    System.IO.File.Delete(txtpath);

                File.WriteAllText(txtpath, output.ToString());
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("impossibile estrarre testo da " + new_files_fullname + " causa " + ex.Message);
            }
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


                        if (file.FullName.ToString().ToLower().EndsWith("xml") || file.FullName.ToString().ToLower().EndsWith("txt"))
                        {

                            


                            Utils.CLogger.WriteLog("start downloading " + file.FullName);
                            DownloadSingleFile(file, LOCAL_DOWNLOAD_FOLDER);
                            Utils.CLogger.WriteLog("end downloading " + file.FullName);
                            if (DELETE_XML_AFTER_DONWLOAD)
                                DeleteXmlAfterDownload(file.FullName);
                        }
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

        private static void DeleteXmlAfterDownload(string fullName)
        {
            try
            {
                Utils.CLogger.WriteLog("deleting " + fullName  + " from sftp remote folder");
                SFTPClient.DeleteFile(fullName);
                Utils.CLogger.WriteLog(fullName + " deleted !!!");
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog(ex);
            }
        }


        private static void DownloadSingleFile(SftpFile file, string plocaldownloadfolder)
        {
            
            string FullLocalPath = System.IO.Path.Combine(plocaldownloadfolder, file.Name);
            string FullLocalPath_new = FullLocalPath;
            try
            {
                if (System.IO.File.Exists(FullLocalPath))
                {
                    /*                  
                                        Utils.CLogger.WriteLog("file " + file.FullName + " already present in destination folder removing it before downloading ");
                                        System.IO.File.Delete(FullLocalPath);
                                        Utils.CLogger.WriteLog("file " + file.FullName + " DELETED !!!! ");
                    */
                    Utils.CLogger.WriteLog("file " + file.FullName + " already present in destination folder renaming with_new_name it before downloading ");
                    FullLocalPath_new = System.IO.Path.Combine(plocaldownloadfolder, "samename_" + file.Name );
                    Utils.CLogger.WriteLog("file " + file.FullName + " downloading file with new name  " + FullLocalPath_new);
                    Utils.CLogger.WriteLog(new Exception("file " + file.FullName + " downloading file with new name  " + FullLocalPath_new));
                }
                using (Stream fileStream = File.OpenWrite(FullLocalPath_new))
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

        private static void MoveToDownloadFolder(string path_to_move)
        {
            SendSSHCommand(path_to_move);
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

        private static void ReadConnectionInfoParameters(string[] args)
        {
            try
            {
/*#if DEBUG


                SFTP_HOST = ConfigurationManager.AppSettings["SFTP_HOST"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                SFTP_USER = ConfigurationManager.AppSettings["SFTP_USER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                SFTP_PWD = ConfigurationManager.AppSettings["SFTP_PWD"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                SFTP_DOWNLOAD_FOLDER = ConfigurationManager.AppSettings["SFTP_DOWNLOAD_FOLDER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                LOCAL_DOWNLOAD_FOLDER = ConfigurationManager.AppSettings["LOCAL_DOWNLOAD_FOLDER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                LOCAL_FILES_FOLDER = ConfigurationManager.AppSettings["LOCAL_FILES_FOLDER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                SFTP_UPLOAD_FOLDER = ConfigurationManager.AppSettings["SFTP_UPLOAD_FOLDER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                LOCAL_UPLOAD_FOLDER = ConfigurationManager.AppSettings["LOCAL_UPLOAD_FOLDER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                LOCAL_PDF_FOLDER = ConfigurationManager.AppSettings["LOCAL_PDF_FOLDER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                LOCAL_TXT_FOLDER = ConfigurationManager.AppSettings["LOCAL_TXT_FOLDER"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                LOCAL_UPLOAD_FOLDER_BACKUP = ConfigurationManager.AppSettings["LOCAL_UPLOAD_FOLDER_BACKUP"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");

                YES_TO_DOWNLOAD = Convert.ToBoolean(ConfigurationManager.AppSettings["YES_TO_DOWNLOAD"].ToString());
                YES_TO_UPLOAD = Convert.ToBoolean(ConfigurationManager.AppSettings["YES_TO_UPLOAD"].ToString());

                DELETE_XML_AFTER_DONWLOAD = Convert.ToBoolean(ConfigurationManager.AppSettings["DELETE_XML_AFTER_DONWLOAD"].ToString());
                EXTRACT_PDF_BEFORE_UPLOAD = Convert.ToBoolean(ConfigurationManager.AppSettings["EXTRACT_PDF_BEFORE_UPLOAD"].ToString());

                SFTP_UPLOAD_FOLDER_CONSERVAZIONE = ConfigurationManager.AppSettings["SFTP_UPLOAD_FOLDER_CONSERVAZIONE"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione"); ;
                LOCAL_UPLOAD_FOLDER_CONSERVAZIONE = ConfigurationManager.AppSettings["LOCAL_UPLOAD_FOLDER_CONSERVAZIONE"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione"); ;
                LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP = ConfigurationManager.AppSettings["LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione"); ;

                LOCAL_DOWNLOAD_FOLDER_ESITI = ConfigurationManager.AppSettings["LOCAL_DOWNLOAD_FOLDER_ESITI"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione"); 
                LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP = ConfigurationManager.AppSettings["LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                SFTP_DOWNLOAD_FOLDER_ESITI = ConfigurationManager.AppSettings["SFTP_DOWNLOAD_FOLDER_ESITI"].Replace(@"E:\SFTPSEND_TEST", @"c:\tmp\fatturazione");
                
                CHECK_ESITI = CheckSettingBool("CHECK_ESITI");
                REMOVE_ESITI_ONCE_DOWNLOAD = CheckSettingBool("REMOVE_ESITI_ONCE_DOWNLOAD");
                YES_TO_UPLOAD_CONSERVAZIONE = CheckSettingBool("YES_TO_UPLOAD_CONSERVAZIONE"); // Convert.ToBoolean(ConfigurationManager.AppSettings["YES_TO_UPLOAD_CONSERVAZIONE"].ToString());

                


#else
*/
                SFTP_HOST = CheckSettingString("SFTP_HOST");
                SFTP_USER = CheckSettingString("SFTP_USER");
                SFTP_PWD = CheckSettingString("SFTP_PWD");
                SFTP_DOWNLOAD_FOLDER = CheckSettingString("SFTP_DOWNLOAD_FOLDER");
                LOCAL_DOWNLOAD_FOLDER = CheckSettingString("LOCAL_DOWNLOAD_FOLDER");
                LOCAL_FILES_FOLDER = CheckSettingString("LOCAL_FILES_FOLDER");
                SFTP_UPLOAD_FOLDER = CheckSettingString("SFTP_UPLOAD_FOLDER");
                LOCAL_UPLOAD_FOLDER = CheckSettingString("LOCAL_UPLOAD_FOLDER");
                LOCAL_PDF_FOLDER = CheckSettingString("LOCAL_PDF_FOLDER");
                LOCAL_TXT_FOLDER = CheckSettingString("LOCAL_TXT_FOLDER");
                LOCAL_UPLOAD_FOLDER_BACKUP = CheckSettingString("LOCAL_UPLOAD_FOLDER_BACKUP");

                YES_TO_DOWNLOAD = CheckSettingBool("YES_TO_DOWNLOAD");
                YES_TO_UPLOAD = CheckSettingBool("YES_TO_UPLOAD");

                DELETE_XML_AFTER_DONWLOAD = CheckSettingBool("DELETE_XML_AFTER_DONWLOAD");
                EXTRACT_PDF_BEFORE_UPLOAD = CheckSettingBool("EXTRACT_PDF_BEFORE_UPLOAD");

                SFTP_UPLOAD_FOLDER_CONSERVAZIONE = CheckSettingString("SFTP_UPLOAD_FOLDER_CONSERVAZIONE");
                LOCAL_UPLOAD_FOLDER_CONSERVAZIONE = CheckSettingString("LOCAL_UPLOAD_FOLDER_CONSERVAZIONE");
                LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP = CheckSettingString("LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP");


                LOCAL_DOWNLOAD_FOLDER_ESITI = CheckSettingString("LOCAL_DOWNLOAD_FOLDER_ESITI");
                LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP = CheckSettingString("LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP");
                SFTP_DOWNLOAD_FOLDER_ESITI = CheckSettingString("SFTP_DOWNLOAD_FOLDER_ESITI");

                CHECK_ESITI = CheckSettingBool("CHECK_ESITI");
                REMOVE_ESITI_ONCE_DOWNLOAD = CheckSettingBool("REMOVE_ESITI_ONCE_DOWNLOAD");
                YES_TO_UPLOAD_CONSERVAZIONE = CheckSettingBool("YES_TO_UPLOAD_CONSERVAZIONE"); // Convert.ToBoolean(ConfigurationManager.AppSettings["YES_TO_UPLOAD_CONSERVAZIONE"].ToString());

                
//#endif


                Utils.CLogger.WriteLog("reading connection info details from config file START");

                Utils.CLogger.WriteLog("SFTP_HOST:"  + SFTP_HOST);
                Utils.CLogger.WriteLog("SFTP_USER:" + SFTP_USER);
                Utils.CLogger.WriteLog("SFTP_PWD:" + SFTP_PWD);
                Utils.CLogger.WriteLog("SFTP_DOWNLOAD_FOLDER:" + SFTP_DOWNLOAD_FOLDER);
                Utils.CLogger.WriteLog("SFTP_UPLOAD_FOLDER:" + SFTP_UPLOAD_FOLDER);
                Utils.CLogger.WriteLog("LOCAL_DOWNLOAD_FOLDER:" + LOCAL_DOWNLOAD_FOLDER);
                Utils.CLogger.WriteLog("LOCAL_FILES_FOLDER:" + LOCAL_FILES_FOLDER);
                Utils.CLogger.WriteLog("LOCAL_PDF_FOLDER:" + LOCAL_PDF_FOLDER);

                Utils.CLogger.WriteLog("YES_TO_DOWNLOAD:" + YES_TO_DOWNLOAD);
                Utils.CLogger.WriteLog("YES_TO_UPLOAD:" + YES_TO_UPLOAD);
                Utils.CLogger.WriteLog("LOCAL_TXT_FOLDER:" + LOCAL_TXT_FOLDER);

                Utils.CLogger.WriteLog("DELETE_XML_AFTER_DONWLOAD:" + DELETE_XML_AFTER_DONWLOAD);
                Utils.CLogger.WriteLog("EXTRACT_PDF_BEFORE_UPLOAD:" + EXTRACT_PDF_BEFORE_UPLOAD);



                Utils.CLogger.WriteLog("SFTP_UPLOAD_FOLDER_CONSERVAZIONE:" + SFTP_UPLOAD_FOLDER_CONSERVAZIONE);
                Utils.CLogger.WriteLog("LOCAL_UPLOAD_FOLDER_CONSERVAZIONE:" + LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP);
                Utils.CLogger.WriteLog("LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP:" + LOCAL_UPLOAD_FOLDER_CONSERVAZIONE_BACKUP);


                Utils.CLogger.WriteLog("LOCAL_DOWNLOAD_FOLDER_ESITI:" + LOCAL_DOWNLOAD_FOLDER_ESITI);
                Utils.CLogger.WriteLog("LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP:" + LOCAL_DOWNLOAD_FOLDER_ESITI_BACKUP);
                Utils.CLogger.WriteLog("SFTP_DOWNLOAD_FOLDER_ESITI:" + SFTP_DOWNLOAD_FOLDER_ESITI);
                Utils.CLogger.WriteLog("CHECK_ESITI:" + CHECK_ESITI);
                Utils.CLogger.WriteLog("REMOVE_ESITI_ONCE_DOWNLOAD:" + REMOVE_ESITI_ONCE_DOWNLOAD);
                Utils.CLogger.WriteLog("YES_TO_UPLOAD_CONSERVAZIONE:" + YES_TO_UPLOAD_CONSERVAZIONE);
                Utils.CLogger.WriteLog("EMAIL_ESITI:" + EMAIL_ESITI);
                

                if (args.Length > 0)
                {
                    Utils.CLogger.WriteLog("FIND COMMAND LINE PARAMETER " + args[0]);
                    Utils.CLogger.WriteLog("FORCING");

                    Utils.CLogger.WriteLog("*****************************************************************************");


                    if ("YES_TO_DOWNLOAD".Equals(args[0]))
                    {
                        YES_TO_DOWNLOAD = true;
                        DELETE_XML_AFTER_DONWLOAD = true;
                    }
                        
                    if ("YES_TO_UPLOAD".Equals(args[0])) YES_TO_UPLOAD = true;
                    if ("YES_TO_UPLOAD_CONSERVAZIONE".Equals(args[0])) YES_TO_UPLOAD_CONSERVAZIONE = true;
                    if ("CHECK_ESITI".Equals(args[0]))
                    {
                        CHECK_ESITI = true;
                        //MOVE_ESITI_ONCE_DOWNLOAD = true;
                    }
                        
                    

                    //DELETE_XML_AFTER_DONWLOAD = false;

                    Utils.CLogger.WriteLog("YES_TO_DOWNLOAD:" + YES_TO_DOWNLOAD);
                    Utils.CLogger.WriteLog("YES_TO_UPLOAD:" + YES_TO_UPLOAD);
                    Utils.CLogger.WriteLog("YES_TO_UPLOAD_CONSERVAZIONE:" + YES_TO_UPLOAD_CONSERVAZIONE);
                    Utils.CLogger.WriteLog("CHECK_ESITI:" + CHECK_ESITI);
                    Utils.CLogger.WriteLog("MOVE_ESITI_ONCE_DOWNLOAD:" + MOVE_ESITI_ONCE_DOWNLOAD);
                    Utils.CLogger.WriteLog("DELETE_XML_AFTER_DONWLOAD:" + DELETE_XML_AFTER_DONWLOAD);
                    



                    Utils.CLogger.WriteLog("*****************************************************************************");

                }





                Utils.CLogger.WriteLog("reading connection info details from config file END");
            }
            catch (Exception ex)
            {
                Utils.CLogger.WriteLog("ReadConnectionInfoParameters(string[] args)");
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
