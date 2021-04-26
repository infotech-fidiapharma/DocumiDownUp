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
# Data:        2018-10-29 - primo giro completo funzionante
#---------------------------------------------------------------------*
*/

Questo eseguibile serve per gestire il seguente flusso

si aggancia all'sftp di documi
procede con lo scaricare le fatture fornitori come file xml dentro una cartella
una volta scaricate estrae da quelle dove è codificata l'informazione le fatture come documenti (pdf, zip e quant'altro)
poi usando un foglio di stile formatta l'xml trasformandolo in un PDF.

procede poi con l'invio delle fatture clienti sempre sul portale documi.

Tutte le configurazioni sono dentro il file App.config sezione settings

In DEBUG mode usa il file debug.config. 

I tag da usare sono i seguenti

    <add key="SFTP_HOST" value="81.174.69.53" />    !--- ip del server di documi
    <add key="SFTP_USER" value="fidia.test" />     -- sftp username 
    <add key="SFTP_PWD" value="UkaKABxibJQ2p7CLZDajgjWB" />    -- sftp password 
    <add key="SFTP_DOWNLOAD_FOLDER" value="/downloads/xml/" /> -- folder remoto dove si trovano gli xml  (fatture passive)
    <add key="SFTP_UPLOAD_FOLDER" value="/downloads/xml/" /> folder remoto dove vanno messi gli xml delle fatture attive
    <add key="LOCAL_DOWNLOAD_FOLDER" value="E:\SFTPSEND_TEST\FATTURAZIONE\IN\xml\" />  folder locale dove finiscono le fatture passive come xml
    <add key="LOCAL_UPLOAD_FOLDER" value="E:\SFTPSEND_TEST\FATTURAZIONE\OUT\xml\" /> folder locale dove finiscono le fatture attive come xml
    <add key="LOCAL_FILES_FOLDER" value="E:\SFTPSEND_TEST\FATTURAZIONE\IN\files\" />
    <add key="LOCAL_PDF_FOLDER" value="E:\SFTPSEND_TEST\FATTURAZIONE\IN\pdf\" />    
    <add key="LOCAL_UPLOAD_FOLDER_BACKUP" value="E:\SFTPSEND_TEST\FATTURAZIONE\OUT\backup\" />    
    <add key="LOCAL_TXT_FOLDER" value="E:\SFTPSEND_TEST\FATTURAZIONE\IN\txt\" />       
    <add key="YES_TO_DOWNLOAD" value="true" />
    <add key="YES_TO_UPLOAD" value="false" />
    <add key="DELETE_XML_AFTER_DONWLOAD" value="true" />
    <add key="EXTRACT_PDF_BEFORE_UPLOAD" value="true" />
