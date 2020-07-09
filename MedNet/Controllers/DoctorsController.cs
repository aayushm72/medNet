﻿using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MedNet.Models;
using MedNet.Data.Models.Models;
using System;
using MedNet.Data.Services;
using MedNet.Data.Models;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Drawing;
using Microsoft.AspNetCore.Authentication;
using System.Net.Sockets;

namespace MedNet.Controllers
{
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public class DoctorsController : Controller
    {
        private readonly ILogger<DoctorsController> _logger;
        private BigChainDbService _bigChainDbService;
        private Random _random;

        public DoctorsController(ILogger<DoctorsController> logger)
        {
            _logger = logger;
            string[] nodes = Globals.nodes;
            _bigChainDbService = new BigChainDbService(nodes);
            _random = new Random();
        }

        public IActionResult Index()
        {
            return View();
        }

        
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(DoctorLoginViewModel indexViewModel)
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (!ModelState.IsValid)
                return View(indexViewModel);
            string signPrivateKey = null, agreePrivateKey = null;
            Assets<UserCredAssetData> userAsset = _bigChainDbService.GetUserAssetFromTypeID(AssetType.Doctor, indexViewModel.DoctorMINC);
            if (userAsset == null)
            {
                ModelState.AddModelError("", "We could not find a matching user");
                return View(indexViewModel);
            }
            var hashedKeys = userAsset.data.Data.PrivateKeys;
            try
            {
                EncryptionService.getPrivateKeyFromIDKeyword(indexViewModel.DoctorMINC, indexViewModel.DoctorKeyword, hashedKeys, out signPrivateKey, out agreePrivateKey);
            }
            catch
            {
                ModelState.AddModelError("", "Keyword may be incorrect");
                return View(indexViewModel);
            }
            UserCredMetadata userMetadata = _bigChainDbService.GetMetadataFromAssetPublicKey<UserCredMetadata>(userAsset.id, EncryptionService.getSignPublicKeyStringFromPrivate(signPrivateKey));
            var password = indexViewModel.password;
            if (EncryptionService.verifyPassword(password, userMetadata.hashedPassword))
            {
                HttpContext.Session.SetString(Globals.currentDSPriK, signPrivateKey);
                HttpContext.Session.SetString(Globals.currentDAPriK, agreePrivateKey);
                HttpContext.Session.SetString(Globals.currentUserName, $"{userAsset.data.Data.FirstName} {userAsset.data.Data.LastName}");
                HttpContext.Session.SetString(Globals.currentUserID, userAsset.data.Data.ID);
                return RedirectToAction("patientLookUp");
            }
            else
            {
                ModelState.AddModelError("", "Password or Keyword incorrect.");
                return View(indexViewModel);
            }
        }

        public IActionResult SignUp()
        {
            return View();
        }

        [HttpPost]
        public IActionResult SignUp(doctorSignUpViewModel doctorSignUpViewModel)
        {
            string signPrivateKey = null, agreePrivateKey = null, signPublicKey = null, agreePublicKey = null;
            Assets<UserCredAssetData> userAsset = _bigChainDbService.GetUserAssetFromTypeID(AssetType.Doctor, doctorSignUpViewModel.DoctorMINC);
            if (userAsset != null)
            {
                ModelState.AddModelError("", "A Doctor profile with that MINC already exists");
                return View(doctorSignUpViewModel);
            }
            var passphrase = doctorSignUpViewModel.DoctorKeyWord;
            var password = doctorSignUpViewModel.Password;
            EncryptionService.getNewBlockchainUser(out signPrivateKey, out signPublicKey, out agreePrivateKey, out agreePublicKey);
            var userAssetData = new UserCredAssetData
            {
                FirstName = doctorSignUpViewModel.FirstName,
                LastName = doctorSignUpViewModel.LastName,
                ID = doctorSignUpViewModel.DoctorMINC,
                Email = doctorSignUpViewModel.Email,
                PrivateKeys = EncryptionService.encryptPrivateKeys(doctorSignUpViewModel.DoctorMINC, passphrase, signPrivateKey, agreePrivateKey),
                DateOfRecord = DateTime.Now,
                SignPublicKey = signPublicKey,
                AgreePublicKey = agreePublicKey,
                FingerprintData = new List<string>(),
            };
            var userMetadata = new UserCredMetadata
            {
                hashedPassword = EncryptionService.hashPassword(password)
            };
            var asset = new AssetSaved<UserCredAssetData>
            {
                Type = AssetType.Doctor,
                Data = userAssetData,
                RandomId = _random.Next(0, 100000)
            };
            var metadata = new MetaDataSaved<UserCredMetadata>
            {
                data = userMetadata
            };

            _bigChainDbService.SendCreateTransactionToDataBase(asset, metadata, signPrivateKey);
            return RedirectToAction("Login");
        }

        public IActionResult PatientLookUp()
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (HttpContext.Session.GetString(Globals.currentDSPriK) == null
                || HttpContext.Session.GetString(Globals.currentDAPriK) == null)
                return RedirectToAction("Login");
            else
                return View();
        }

        [HttpPost]
        public IActionResult PatientLookUp(PatientLookupViewModel patientLookupViewModel)
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (!ModelState.IsValid)
                return View(patientLookupViewModel);
            Assets<UserCredAssetData> userAsset = _bigChainDbService.GetUserAssetFromTypeID(AssetType.Patient, patientLookupViewModel.PHN);
            if (userAsset == null)
            {
                ModelState.AddModelError("", "We could not find a matching user");
                return View(patientLookupViewModel);
            }
            HttpContext.Session.SetString(Globals.currentPSPubK, userAsset.data.Data.SignPublicKey);
            HttpContext.Session.SetString(Globals.currentPAPubK, userAsset.data.Data.AgreePublicKey);
            HttpContext.Session.SetString(Globals.currentPPHN, userAsset.data.Data.ID);
            return RedirectToAction("PatientOverview");
        }

        public IActionResult PatientOverview()
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (HttpContext.Session.GetString(Globals.currentDSPriK) == null || HttpContext.Session.GetString(Globals.currentDAPriK) == null)
                return RedirectToAction("Login");
            else if (HttpContext.Session.GetString(Globals.currentPSPubK) == null || HttpContext.Session.GetString(Globals.currentPAPubK) == null)
                return RedirectToAction("patientLookUp");
            else
            {
                Assets<UserCredAssetData> userAsset = _bigChainDbService.GetUserAssetFromTypeID(AssetType.Patient, HttpContext.Session.GetString(Globals.currentPPHN));

                var doctorSignPrivateKey = HttpContext.Session.GetString(Globals.currentDSPriK);
                var doctorAgreePrivateKey = HttpContext.Session.GetString(Globals.currentDAPriK);
                var doctorSignPublicKey = EncryptionService.getSignPublicKeyStringFromPrivate(doctorSignPrivateKey);
                var patientSignPublicKey = HttpContext.Session.GetString(Globals.currentPSPubK);

                var doctorNotesList = _bigChainDbService.GetAllTypeRecordsFromDPublicPPublicKey<string>
                    (AssetType.DoctorNote, doctorSignPublicKey, patientSignPublicKey);
                var prescriptionsList = _bigChainDbService.GetAllTypeRecordsFromDPublicPPublicKey<string>
                    (AssetType.Prescription, doctorSignPublicKey, patientSignPublicKey);
                var doctorNotes = new List<DoctorNote>();
                var prescriptions = new List<Prescription>();
                foreach (var doctorNote in doctorNotesList)
                {
                    var hashedKey = doctorNote.metadata.data[doctorSignPublicKey];
                    var dataDecryptionKey = EncryptionService.getDecryptedEncryptionKey(hashedKey, doctorAgreePrivateKey);
                    var data = EncryptionService.getDecryptedAssetData(doctorNote.data.Data, dataDecryptionKey);
                    doctorNotes.Add(JsonConvert.DeserializeObject<DoctorNote>(data));
                }
                foreach (var prescription in prescriptionsList)
                {
                    var hashedKey = prescription.metadata.data[doctorSignPublicKey];
                    var dataDecryptionKey = EncryptionService.getDecryptedEncryptionKey(hashedKey, doctorAgreePrivateKey);
                    var data = EncryptionService.getDecryptedAssetData(prescription.data.Data, dataDecryptionKey);
                    prescriptions.Add(JsonConvert.DeserializeObject<Prescription>(data));
                }
                var patientInfo = userAsset.data.Data;
                var patientAge = DateTime.Now.Year - patientInfo.DateOfBirth.Year;
                var patientOverviewViewModel = new PatientOverviewViewModel
                {
                    PatientName = $"{patientInfo.FirstName} {patientInfo.LastName}",
                    PatientPHN = patientInfo.ID,
                    PatientDOB = patientInfo.DateOfBirth,
                    PatientAge = patientInfo.DateOfBirth.CalculateAge(),
                    DoctorNotes = doctorNotes.OrderByDescending(d => d.DateOfRecord).ToList(),
                    Prescriptions = prescriptions.OrderByDescending(p => p.PrescribingDate).ToList()
                };

                return View(patientOverviewViewModel);
            }
        }

        public IActionResult AddNewPatientRecord()
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (HttpContext.Session.GetString(Globals.currentDSPriK) == null || HttpContext.Session.GetString(Globals.currentDAPriK) == null)
                return RedirectToAction("Login");
            else if (HttpContext.Session.GetString(Globals.currentPSPubK) == null || HttpContext.Session.GetString(Globals.currentPAPubK) == null)
                return RedirectToAction("patientLookUp");
            else
                return View();
        }

        [HttpPost]
        public IActionResult AddNewPatientRecord(AddNewPatientRecordViewModel addNewPatientRecordViewModel)
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (!string.IsNullOrEmpty(addNewPatientRecordViewModel.DoctorsNote.PurposeOfVisit))
            {
                var noteViewModel = addNewPatientRecordViewModel.DoctorsNote;
                var doctorNote = new DoctorNote
                {
                    PurposeOfVisit = noteViewModel.PurposeOfVisit,
                    Description = noteViewModel.Description,
                    FinalDiagnosis = noteViewModel.FinalDiagnosis,
                    FurtherInstructions = noteViewModel.FurtherInstructions,
                    DoctorName = HttpContext.Session.GetString(Globals.currentUserName),
                    DoctorMinsc = HttpContext.Session.GetString(Globals.currentUserID),
                    DateOfRecord = DateTime.Now
                };
                string encryptionKey;
                var encryptedData = EncryptionService.getEncryptedAssetData(JsonConvert.SerializeObject(doctorNote), out encryptionKey);

                var asset = new AssetSaved<string>
                {
                    Data = encryptedData,
                    RandomId = _random.Next(0, 100000),
                    Type = AssetType.DoctorNote
                };

                var metadata = new MetaDataSaved<Dictionary<string, string>>();
                metadata.data = new Dictionary<string, string>();

                //store the data encryption key in metadata encrypted with sender and reciever agree key
                var doctorSignPrivateKey = HttpContext.Session.GetString(Globals.currentDSPriK);
                var doctorAgreePrivateKey = HttpContext.Session.GetString(Globals.currentDAPriK);
                var patientAgreePublicKey = HttpContext.Session.GetString(Globals.currentPAPubK);
                var patientSignPublicKey = HttpContext.Session.GetString(Globals.currentPSPubK);
                var doctorSignPublicKey = EncryptionService.getSignPublicKeyStringFromPrivate(doctorSignPrivateKey);
                var doctorAgreePublicKey = EncryptionService.getAgreePublicKeyStringFromPrivate(doctorAgreePrivateKey);
                metadata.data[doctorSignPublicKey] =
                    EncryptionService.getEncryptedEncryptionKey(encryptionKey, doctorAgreePrivateKey, doctorAgreePublicKey);
                metadata.data[patientSignPublicKey] =
                    EncryptionService.getEncryptedEncryptionKey(encryptionKey, doctorAgreePrivateKey, patientAgreePublicKey);

                _bigChainDbService.SendCreateTransferTransactionToDataBase<string, Dictionary<string, string>>(asset, metadata, doctorSignPrivateKey, patientSignPublicKey);
            }

            if (!string.IsNullOrEmpty(addNewPatientRecordViewModel.Prescription.DrugNameStrength))
            {
                var prescriptionViewModel = addNewPatientRecordViewModel.Prescription;
                var prescription = new Prescription
                {
                    PrescribingDate = prescriptionViewModel.PrescribingDate,
                    DrugNameStrength = prescriptionViewModel.DrugNameStrength,
                    Dosage = prescriptionViewModel.Dosage,
                    StartDate = prescriptionViewModel.StartDate,
                    EndDate = prescriptionViewModel.EndDate,
                    DoctorName = HttpContext.Session.GetString(Globals.currentUserName),
                    DoctorMinsc = HttpContext.Session.GetString(Globals.currentUserID),
                    DirectionForUse = prescriptionViewModel.DirectionForUse
                };

                string encryptionKey;
                var encryptedData = EncryptionService.getEncryptedAssetData(JsonConvert.SerializeObject(prescription), out encryptionKey);

                var asset = new AssetSaved<string>
                {
                    Data = encryptedData,
                    RandomId = _random.Next(0, 100000),
                    Type = AssetType.Prescription
                };

                var metadata = new MetaDataSaved<Dictionary<string, string>>();
                metadata.data = new Dictionary<string, string>();

                //store the data encryption key in metadata encrypted with sender and reciever agree key
                var doctorSignPrivateKey = HttpContext.Session.GetString(Globals.currentDSPriK);
                var doctorAgreePrivateKey = HttpContext.Session.GetString(Globals.currentDAPriK);
                var patientAgreePublicKey = HttpContext.Session.GetString(Globals.currentPAPubK);
                var patientSignPublicKey = HttpContext.Session.GetString(Globals.currentPSPubK);
                var doctorSignPublicKey = EncryptionService.getSignPublicKeyStringFromPrivate(doctorSignPrivateKey);
                var doctorAgreePublicKey = EncryptionService.getAgreePublicKeyStringFromPrivate(doctorAgreePrivateKey);
                metadata.data[doctorSignPublicKey] =
                    EncryptionService.getEncryptedEncryptionKey(encryptionKey, doctorAgreePrivateKey, doctorAgreePublicKey);
                metadata.data[patientSignPublicKey] =
                    EncryptionService.getEncryptedEncryptionKey(encryptionKey, doctorAgreePrivateKey, patientAgreePublicKey);

                _bigChainDbService.SendCreateTransferTransactionToDataBase<string, Dictionary<string, string>>(asset, metadata, doctorSignPrivateKey, patientSignPublicKey);
            }

            return RedirectToAction("PatientOverview");
        }

        public IActionResult RequestAccess()
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (HttpContext.Session.GetString(Globals.currentDSPriK) == null || HttpContext.Session.GetString(Globals.currentDAPriK) == null)
                return RedirectToAction("Login");
            else if (HttpContext.Session.GetString(Globals.currentPSPubK) == null || HttpContext.Session.GetString(Globals.currentPAPubK) == null)
                return RedirectToAction("patientLookUp");
            else
                return View();
        }

        [HttpPost]
        public IActionResult RequestAccess(RequestAccessViewModel requestAccessViewModel)
        {
            // Description: Authenticates a patient's identity when a Doctor requests access to their medical information
            // Get's the Doctor's information for current session
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            if (!ModelState.IsValid)
                return View(requestAccessViewModel);
            string PHN = HttpContext.Session.GetString(Globals.currentPPHN);
            string patientSignPublicKey = HttpContext.Session.GetString(Globals.currentPSPubK);
            string doctorSignPrivatekey = HttpContext.Session.GetString(Globals.currentDSPriK);
            string doctorSignPublicKey = EncryptionService.getSignPublicKeyStringFromPrivate(doctorSignPrivatekey);
            string doctorAgreePrivatekey = HttpContext.Session.GetString(Globals.currentDAPriK);
            string doctorAgreePublicKey = EncryptionService.getAgreePublicKeyStringFromPrivate(doctorAgreePrivatekey);
            string keyword = requestAccessViewModel.keyword;

            // Searches for a patient with the specified PHN
            Assets<UserCredAssetData> userAsset = _bigChainDbService.GetUserAssetFromTypeID(AssetType.Patient, PHN);
            if (userAsset == null)
            {
                ModelState.AddModelError("", "Could not find a patient profile with PHN: " + PHN);
                return View(requestAccessViewModel);
            }

            // Send request to the Client Computer to authenticate with fingerprint
            int numScans = 1;
            List<Image> fpList = FingerprintService.authenticateFP("24.84.225.22", numScans); // DEBUG: Jacob's Computer 

            // Check if fingerprint data is valid
            if (fpList.Count < numScans)
            {
                ModelState.AddModelError("", "Something went wrong with the fingerprint scan, try again.");
                return View(requestAccessViewModel);
            }
            Image fpImg = fpList[0];

            // Decrypt the patient's fingerprint data stored in the Blockchain
            byte[] dbFpData = null;
            string patientSignPrivateKey, patientAgreePrivateKey;
            List<string> dbList = userAsset.data.Data.FingerprintData;
            List<Image> dbfpList = new List<Image>();
            try
            {
                foreach (string db in dbList)
                {
                    EncryptionService.decryptFingerprintData(PHN, keyword, db, out dbFpData);
                    dbfpList.Add(FingerprintService.byteToImg(dbFpData));
                }
                EncryptionService.getPrivateKeyFromIDKeyword(PHN, keyword, userAsset.data.Data.PrivateKeys, out patientSignPrivateKey, out patientAgreePrivateKey);
            }
            catch
            {
                ModelState.AddModelError("", "Keyword may be incorrect");
                return View(requestAccessViewModel);
            }

            // Compare the scanned fingerprint with the one saved in the database 
            if (!FingerprintService.compareFP(fpImg, fpList))
            {
                ModelState.AddModelError("", "The fingerprint did not match, try again.");
                return View(requestAccessViewModel);
            }

            // Choose the types of records we want to get
            AssetType[] typeList = { AssetType.DoctorNote, AssetType.Prescription };
            var recordList = _bigChainDbService.GetAllTypeRecordsFromPPublicKey<string>
                (typeList, patientSignPublicKey);
            foreach (var record in recordList)
            {
                MetaDataSaved<Dictionary<string, string>> metadata = record.metadata;
                if (!metadata.data.Keys.Contains(doctorSignPublicKey))
                {
                    var hashedKey = metadata.data[patientSignPublicKey];
                    var dataDecryptionKey = EncryptionService.getDecryptedEncryptionKey(hashedKey, patientAgreePrivateKey);
                    var newHash = EncryptionService.getEncryptedEncryptionKey(dataDecryptionKey, patientAgreePrivateKey, doctorAgreePublicKey);
                    metadata.data[doctorSignPublicKey] = newHash;
                    _bigChainDbService.SendTransferTransactionToDataBase(record.id, metadata,
                        patientSignPrivateKey, patientSignPublicKey, record.transID);
                }
            }
            return RedirectToAction("PatientOverview");
        }

        public IActionResult PatientSignUp()
        {
            return View();
        }


        [HttpPost]
        public IActionResult PatientSignUp(PatientSignUpViewModel patientSignUpViewModel)
        {
            // Description: Registers a patient up for a MedNet account
            string signPrivateKey = null, agreePrivateKey = null, signPublicKey = null, agreePublicKey = null;
            Assets<UserCredAssetData> userAsset = _bigChainDbService.GetUserAssetFromTypeID(AssetType.Patient, patientSignUpViewModel.PHN);

            // Check if PHN is already in use
            if (userAsset != null)
            {
                ModelState.AddModelError("", "A Patient profile with that PHN already exists");
                return View(patientSignUpViewModel);
            }

            // Register fingerprint information 
            int numScans = 5;
            List<Image> fpList = FingerprintService.authenticateFP("24.84.225.22", numScans);
            List<byte[]> fpdb = new List<byte[]>();

            if (fpList.Count > numScans)
            {
                ModelState.AddModelError("", "Something went wrong with the fingerprint scan, try again.");
                return View(patientSignUpViewModel);
            }

            // Parse the input data for user registration 
            var passphrase = patientSignUpViewModel.KeyWord;
            var password = patientSignUpViewModel.Password;

            // Encrypt fingerprint data
            List<string> encrList = new List<string>();
            foreach (byte[] fp in fpdb)
            {
                string encrStr = EncryptionService.encryptFingerprintData(patientSignUpViewModel.PHN, passphrase, fp);
                encrList.Add(encrStr);
            }

            // Create a user for the Blockchain 
            EncryptionService.getNewBlockchainUser(out signPrivateKey, out signPublicKey, out agreePrivateKey, out agreePublicKey);

            // Create the user Asset 
            var userAssetData = new UserCredAssetData
            {
                FirstName = patientSignUpViewModel.FirstName,
                LastName = patientSignUpViewModel.LastName,
                ID = patientSignUpViewModel.PHN,
                Email = patientSignUpViewModel.Email,
                DateOfBirth = patientSignUpViewModel.DateOfBirth,
                PrivateKeys = EncryptionService.encryptPrivateKeys(patientSignUpViewModel.PHN, passphrase, signPrivateKey, agreePrivateKey),
                DateOfRecord = DateTime.Now,
                SignPublicKey = signPublicKey,
                AgreePublicKey = agreePublicKey,
                FingerprintData = encrList,
            };

            // Encrypt the user's password in the metadata
            var userMetadata = new UserCredMetadata
            {
                hashedPassword = EncryptionService.hashPassword(password)
            };

            // Save the user Asset and Metadata
            var asset = new AssetSaved<UserCredAssetData>
            {
                Type = AssetType.Patient,
                Data = userAssetData,
                RandomId = _random.Next(0, 100000)
            };
            var metadata = new MetaDataSaved<UserCredMetadata>
            {
                data = userMetadata
            };

            // Send the user's information to the Blockchain database
            _bigChainDbService.SendCreateTransactionToDataBase(asset, metadata, signPrivateKey);
            return RedirectToAction("Login");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return View();
        }
    }
}