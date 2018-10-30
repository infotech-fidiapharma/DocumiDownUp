/*
#

# Copyright © Fidia Farmaceutici s.p.a. - All Rights Reserved
#
# Unauthorized copying of this file, via any medium is strictly prohibited
# Proprietary and confidential
#
# Written by Enrico Ponchio <eponchio@fidiapharma.it>, Jul 2018
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
