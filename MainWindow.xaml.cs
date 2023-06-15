using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Xml.Linq;
using System.Globalization;
using Newtonsoft.Json;

namespace VendorEfilingSample
{
    /// <summary>
    /// Submitting an e-filing document to NetFile's agency system is a three step process:
    /// 1) Submit an e-filing job to the system with the content and credentials required.
    /// 2) If the system accepts the e-filing job for processing, poll the server to wait for job completion
    /// 3) Once the job is completed, fetch the filing results from the server (to determine if the e-filing was accepted)
    /// </summary>
    public partial class MainWindow : Window
    {
        const string DATE_TIME_FORMAT = "MM/dd/yyyy HH:mm:ss";
        /// <summary>
        /// Where do we send our API requests by default?  SSL connections are REQUIRED.
        /// </summary>
        //const string DEFAULT_REMOTE_V10 = "https://netfile.com/filer/vendor/api/v10/";
        //const string DEFAULT_REMOTE_V11 = "https://netfile.com/filer/vendor/api/v11/";
        const string DEFAULT_REMOTE_V10 = "http://localhost:53128/vendor/api/v10/";
        const string DEFAULT_REMOTE_V11 = "http://localhost:53128/vendor/api/v11/";
        /// <summary>
        /// The remote method name for our 'was the e-filing accepted?' function
        /// </summary>
        const string METHOD_RESULT = "EfilingResult";
        /// <summary>
        /// The remote method name for our 'check the job status' function
        /// </summary>
        const string METHOD_STATUS = "CheckJobStatus";
        /// <summary>
        /// The remote method name for our 'submit the e-file' function
        /// </summary>        
        const string METHOD_SUBMIT = "SubmitEfile";
        /// <summary>
        /// Don't get stuck in a status-checking loop forever
        /// </summary>
        const int MAXIMUM_NUMER_OF_STATUS_CHECKS = 10;
        /// <summary>
        /// Wait two seconds between status checks
        /// </summary>
        const int STATUS_CHECK_INTERVAL = 2000;

        Guid jobId;

        /// <summary>
        /// Everything in here is optional.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            //
            this.Top = Properties.Settings.Default.Top;
            this.Left = Properties.Settings.Default.Left;
            this.Height = Properties.Settings.Default.Height;
            this.Width = Properties.Settings.Default.Width;
            //
            txtFilerId.Text = Properties.Settings.Default.FilerId;
            txtPassword.Text = Properties.Settings.Default.FilerPassword;
            if (String.IsNullOrEmpty(Properties.Settings.Default.RemoteRoot))
                txtRemote.Text = DEFAULT_REMOTE_V11;
            else
                txtRemote.Text = Properties.Settings.Default.RemoteRoot;
            txtReplyTo.Text = Properties.Settings.Default.ReplyTo;
            txtSupercedes.Text = Properties.Settings.Default.Supercedes;
            txtVendorId.Text = Properties.Settings.Default.VendorId;
            txtVendorPIN.Text = Properties.Settings.Default.VendorPIN;
            //
            jobId = Guid.Empty;
        }

        /// <summary>
        /// More optional work, to improve the user experience for this UI
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Properties.Settings.Default.FilerId = txtFilerId.Text;
            Properties.Settings.Default.FilerPassword = txtPassword.Text;
            Properties.Settings.Default.RemoteRoot = txtRemote.Text;
            Properties.Settings.Default.ReplyTo = txtReplyTo.Text;
            Properties.Settings.Default.Supercedes = txtSupercedes.Text;
            Properties.Settings.Default.VendorId = txtVendorId.Text;
            Properties.Settings.Default.VendorPIN = txtVendorPIN.Text;
            Properties.Settings.Default.Top = this.Top;
            Properties.Settings.Default.Left = this.Left;
            Properties.Settings.Default.Height = this.Height;
            Properties.Settings.Default.Width = this.Width;
            //
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Just a nice shortcut.  Not related to actual e-file submission
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnSelect_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            if (!String.IsNullOrEmpty(Properties.Settings.Default.DirectoryPath))
                openDialog.InitialDirectory = Properties.Settings.Default.DirectoryPath;
            if (openDialog.ShowDialog().Value)
            {
                Properties.Settings.Default.DirectoryPath = System.IO.Path.GetDirectoryName(openDialog.FileName);
                txtFilePath.Text = openDialog.FileName;
            }
        }

        private void Log(string message)
        {
            const string TIME_FORMAT = "HH:mm:ss.fff tt";
            txtLog.AppendText(String.Format("{0} {1}", DateTime.Now.ToString(TIME_FORMAT), message) + "\r\n");
        }

        private void ParseSubmitResponse(string response)
        {
            jobId = Guid.Empty;
            //<response agency="NetFile" type="netfile.asp.mvc.BlackMesa.Areas.Vendor.Models.APISubmitEfileResponse" version="1.0">
            //<accepted>true</accepted>
            //<job_id>ba3bdf17-cc63-441f-9bab-a12a010b08d1</job_id>
            //</response>
            //
            // read job id value from response document
            // set the jobid field accordingly
            //
            // Wow!  This is some mighty bad sample XML parsing code! :)
            XDocument xdoc = XDocument.Parse(response);
            if (xdoc.Root.Element("accepted") != null && xdoc.Root.Element("job_id") != null)
            {
                Log("Received response from server");
                bool accepted = false;
                bool.TryParse(xdoc.Root.Element("accepted").Value, out accepted);
                if (accepted)
                {
                    jobId = new Guid(xdoc.Root.Element("job_id").Value);
                    Log(String.Format("Job accepted as '{0}'", jobId.ToString()));
                }
                else
                {
                    if (xdoc.Element("error_message") != null)
                    {
                        string errorMessage = xdoc.Root.Element("error_message").Value;
                        Log(String.Format("Job not accepted.  Reason : {0}", errorMessage));
                    }
                    else
                        Log("Job not accepted");
                }
            }
        }

        private void ParseResultResponse(string response, out EFilingResult accepted, out string filingId, out DateTime filingDate,
            out string validationXmlContent)
        {
            //<response agency="NetFile" type="netfile.asp.mvc.BlackMesa.Areas.Vendor.Models.APIEfilingResultResponse" version="1.0">
            //  <status>2</status>
            //  <filing_date>01/01/0001 00:00:00</filing_date>
            //  <filing_id>12345678</filing_id>
            //  <validation_content>(Base-64-encoded Xml document content, can be optionally parsed by calling application)</validation_content>
            //</response>
            accepted = EFilingResult.Unknown;
            filingId = String.Empty;
            filingDate = DateTime.MinValue;
            validationXmlContent = String.Empty;

            XDocument xdoc = XDocument.Parse(response);
            if (xdoc.Root.Element("status") != null)
            {
                int statusVal = -1;
                int.TryParse(xdoc.Root.Element("status").Value, out statusVal);
                accepted = (EFilingResult)statusVal;
                if (accepted == EFilingResult.Accepted || accepted == EFilingResult.Pending)
                {
                    filingDate = xdoc.Root.Element("filing_date") == null ? DateTime.MinValue : DateTime.ParseExact(xdoc.Root.Element("filing_date").Value, DATE_TIME_FORMAT, CultureInfo.InvariantCulture);
                    filingId = xdoc.Root.Element("filing_id") == null ? String.Empty : xdoc.Root.Element("filing_id").Value;
                }
                string encodedValidationXmlContent = xdoc.Root.Element("validation_content") == null ? String.Empty : xdoc.Root.Element("validation_content").Value;
                validationXmlContent = UTF8Encoding.UTF8.GetString(Convert.FromBase64String(encodedValidationXmlContent));
                // if you wanted to display warnings and errors to the user, you could parse the validation output at this point...
            }
        }

        private StatusCheckResult ParseStatusResponse(string response)
        {
            StatusCheckResult result = StatusCheckResult.Unknown;
            //<response agency="NetFile" type="netfile.asp.mvc.BlackMesa.Areas.Vendor.Models.APIJobStatusResponse" version="1.0">
            //<job_status_code>0</job_status_code>
            //<job_status_detail>waiting</job_status_detail>
            //</response>
            //
            XDocument xdoc = XDocument.Parse(response);
            if (xdoc.Root.Element("job_status_code") != null)
            {
                int value = int.MinValue;
                int.TryParse(xdoc.Root.Element("job_status_code").Value, out value);
                // Waiting = 0, The job is in queue to be processed, or being processed
                // Success = 1, The job completed processing
                // Failure = -1, something bad happened
                // IdUnknown = -2, this is not the job you're looking for...
                switch (value)
                {
                    case -2:
                        result = StatusCheckResult.UnknownJobId;
                        break;
                    case -1:
                        result = StatusCheckResult.FailedToProcess;
                        break;
                    case 0:
                        result = StatusCheckResult.Working;
                        break;
                    case 1:
                        result = StatusCheckResult.ProcessingComplete;
                        break;
                    default:
                        break;
                }
            }
            //
            return result;
        }

        /// <summary>
        /// POST content is not required to be JSON, name/value content pairs are also allowed.
        /// This is just more convenient for an example.
        /// </summary>
        /// <param name="base64EncodedFilingContent"></param>
        /// <returns></returns>
        string PopulateSubmissionJson(string base64EncodedFilingContent)
        {
            var model = new SubmitEfileModel();
            model.VendorId = txtVendorId.Text;
            model.VendorPin = txtVendorPIN.Text;
            model.FilerId = txtFilerId.Text;
            model.FilerPassword = txtPassword.Text;
            model.Email = txtReplyTo.Text;
            if (!string.IsNullOrEmpty(txtSupercedes.Text))
                model.SupercededFilingId = txtSupercedes.Text;
            model.Base64EncodedEfile = base64EncodedFilingContent;
            if (!string.IsNullOrEmpty(txtSignerId1.Text))
            {
                model.Signatures.Add(
                    new SubmitSignerPin()
                    {
                        SignerId = txtSignerId1.Text,
                        SignerPin = txtSignerPin1.Text
                    });
            }
            if (!string.IsNullOrEmpty(txtSignerId2.Text))
            {
                model.Signatures.Add(
                    new SubmitSignerPin()
                    {
                        SignerId = txtSignerId2.Text,
                        SignerPin = txtSignerPin2.Text
                    });
            }
            return JsonConvert.SerializeObject(model, Formatting.Indented);
        }

        /// <summary>
        /// Here's the guts of submitting an e-file.  Everything else in the file is just supporting this method...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            jobId = Guid.Empty;
            string submitUrl = txtRemote.Text + METHOD_SUBMIT;
            // create parameter values
            string unencodedFile = System.IO.File.ReadAllText(txtFilePath.Text);
            // encode .CAL file to base-64 string
            string encodedFile = System.Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(unencodedFile));
            // upload e-file to NetFile API
            Log(String.Format("Sending file '{0}' to URL '{1}'", txtFilePath.Text, submitUrl));
            using (WebClient client = new WebClient())
            {
                var responseString = string.Empty;
                if (rb10.IsChecked.HasValue && rb10.IsChecked.Value == true)
                {
                   // use version 1.0 of the Vendor API
                    byte[] response = client.UploadValues(submitUrl, new NameValueCollection()
                    {
                        {"VendorId", txtVendorId.Text},
                        {"VendorPIN", txtVendorPIN.Text},
                        {"FilerId", txtFilerId.Text},
                        {"FilerPassword", txtPassword.Text},
                        {"Email", txtReplyTo.Text},
                        // we only need to provide the superceded filing id for amendments
                        // but it won't hurt to submit a blank value
                        {"SupercededFilingId", txtSupercedes.Text},
                        {"Base64EncodedEfile", encodedFile}
                    });
                    responseString = Encoding.ASCII.GetString(response);
                }
                else if (rb11.IsChecked.HasValue && rb11.IsChecked.Value == true)
                {
                    // use version 1.1 of the Vendor API
                    var requestJson = PopulateSubmissionJson(encodedFile);
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";
                    responseString = client.UploadString(submitUrl, requestJson);
                }
                else
                {
                    throw new Exception("Something bad just happened to the UI");
                }
                // process the response
                ParseSubmitResponse(responseString);
                // if our submission was accepted for processing, we should check to see when
                // processing is completed
                if (jobId != Guid.Empty)
                {
                    for (int i = 1; i <= MAXIMUM_NUMER_OF_STATUS_CHECKS; i++)
                    {
                        System.Threading.Thread.Sleep(STATUS_CHECK_INTERVAL);
                        // check to see if our job completed
                        // if it's completed, let's find out if our e-filing was accepted or rejected
                        StatusCheckResult currentStatus = CheckJobStatus(jobId);
                        if (currentStatus == StatusCheckResult.FailedToProcess || currentStatus == StatusCheckResult.UnknownJobId)
                        {
                            Log("Status: the job failed to process");
                            break;
                        }
                        else if (currentStatus == StatusCheckResult.ProcessingComplete)
                        {
                            Log("Status: the job was processed");
                            //
                            EFilingResult accepted = EFilingResult.Unknown;
                            string filingId = String.Empty;
                            DateTime filingDate = DateTime.MinValue;
                            string validationXmlContent = String.Empty;
                            GetEfilingResult(jobId, out accepted, out filingId, out filingDate, out validationXmlContent);
                            //
                            if (accepted == EFilingResult.Accepted)
                                Log(String.Format("E-filing accepted as filing id '{0}'", filingId));
                            if (accepted == EFilingResult.Pending)
                                Log(String.Format("E-filing pending signature verification as pending id '{0}'", filingId));
                            else
                                Log("E-filing rejected");
                            // at this point, we could parse the validation output to see if it was a validation
                            // error that prevented the acceptance of the filing.  If it wasn't a validation error,
                            // then it was rejected for a filing context error, such as:
                            // 1) the filer id/password combination was invalid
                            // 2) the filer isn't allowed to file this document
                            // 3) an amendment was out of sequence, or the superceded filing wasn't found
                            // ...etc...
                            break;
                        }
                        // if that was the last check, we're giving up
                        if (i == MAXIMUM_NUMER_OF_STATUS_CHECKS)
                            Log("Timeout waiting for job completion");
                        // if the job isn't finished, let's wait some more
                        i++;
                    }
                }
            }
            Log("Process complete");
        }

        private StatusCheckResult CheckJobStatus(Guid jobId)
        {
            StatusCheckResult result = StatusCheckResult.Unknown;
            string statusUrl = txtRemote.Text + METHOD_STATUS + "/" + jobId.ToString();
            using (WebClient client = new WebClient())
            {
                try
                {
                    result = ParseStatusResponse(client.DownloadString(statusUrl));
                }
                catch
                {
                    // do nothing
                }
            }
            return result;
        }

        enum EFilingResult
        {
            /// <summary>
            /// We don't know the outcome of the e-filing submission (aasume rejection!)
            /// </summary>
            Unknown = 0,
            /// <summary>
            /// The e-filing result was a filing that requires further signature verification
            /// </summary>
            Pending = 1,
            /// <summary>
            /// The e-filing was accepted
            /// </summary>
            Accepted = 2,
            /// <summary>
            /// The e-filing was rejected
            /// </summary>
            Rejected = 3
        }

        private void GetEfilingResult(Guid jobId, out EFilingResult accepted, out string filingId, out DateTime filingDate,
            out string validationXmlContent)
        {
            accepted = EFilingResult.Unknown;
            filingId = String.Empty;
            filingDate = DateTime.MinValue;
            validationXmlContent = String.Empty;
            //
            string resultUrl = txtRemote.Text + METHOD_RESULT + "/" + jobId.ToString();
            using (WebClient client = new WebClient())
            {
                try
                {
                    ParseResultResponse(client.DownloadString(resultUrl), out accepted, out filingId,
                        out filingDate, out validationXmlContent);
                }
                catch
                {
                    // do nothing
                }
            }
        }

        private void rb10_Checked(object sender, RoutedEventArgs e)
        {
            txtRemote.Text = DEFAULT_REMOTE_V10;
            lblSigner1.Visibility = Visibility.Hidden;
            txtSignerId1.Visibility = Visibility.Hidden;
            txtSignerPin1.Visibility = Visibility.Hidden;
            lblSigner2.Visibility = Visibility.Hidden;
            txtSignerId2.Visibility = Visibility.Hidden;
            txtSignerPin2.Visibility = Visibility.Hidden;
            lblOptional1.Visibility = Visibility.Hidden;
            lblOptional2.Visibility = Visibility.Hidden;
        }

        private void rb11_Checked(object sender, RoutedEventArgs e)
        {
            txtRemote.Text = DEFAULT_REMOTE_V11;
            lblSigner1.Visibility = Visibility.Visible;
            txtSignerId1.Visibility = Visibility.Visible;
            txtSignerPin1.Visibility = Visibility.Visible;
            lblSigner2.Visibility = Visibility.Visible;
            txtSignerId2.Visibility = Visibility.Visible;
            txtSignerPin2.Visibility = Visibility.Visible;
            lblOptional1.Visibility = Visibility.Visible;
            lblOptional2.Visibility = Visibility.Visible;
        }
    }

    enum StatusCheckResult
    {
        Unknown = -3,
        UnknownJobId = -2,
        FailedToProcess = -1,
        Working = 0,
        ProcessingComplete = 1
    }
}
