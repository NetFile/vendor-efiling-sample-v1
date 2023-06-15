using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VendorEfilingSample
{
    public class SubmitSignerPin
    {
        public string SignerId { get; set; }
        public string SignerPin { get; set; }

        public SubmitSignerPin()
        {
            SignerId = string.Empty;
            SignerPin = string.Empty;
        }
    }

    public class SubmitEfileModel
    {
        public string VendorId { get; set; }
        public string VendorPin { get; set; }
        public string FilerId { get; set; }
        public string FilerPassword { get; set; }
        public string Email { get; set; }
        public string SupercededFilingId { get; set; }
        public string Base64EncodedEfile { get; set; }
        public int? AmendmentSequence { get; set; }
        public List<SubmitSignerPin> Signatures { get; set; }

        public SubmitEfileModel()
        {
            VendorId = string.Empty;
            VendorPin = string.Empty;
            FilerId = String.Empty;
            FilerPassword = String.Empty;
            Email = String.Empty;
            SupercededFilingId = String.Empty;
            Base64EncodedEfile = String.Empty;
            AmendmentSequence = null;
            Signatures = new List<SubmitSignerPin>();
        }
    }
}
