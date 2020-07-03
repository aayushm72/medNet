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
using Org.BouncyCastle.Asn1;
using System.Text;
using MongoDB.Driver;
using System.Net.Sockets;

namespace MedNet.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private BigChainDbService _bigChainDbService;
        private Random _random;

        public HomeController(ILogger<HomeController> logger)
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

        public IActionResult TestFingerprintButton(TestFingerprintButton model)
        {
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            return View(model);
        }

        public IActionResult TriggerFingerprint()
        {
            // DEBUG:JW
            // Description: Using for DEBUG. URL: https://lifeblocks.site/home/testfingerprintbutton 
            ViewBag.DoctorName = HttpContext.Session.GetString(Globals.currentUserName);
            
            // Retrieve the Public IP of the Client Computer using the browser
            var ip = HttpContext.Connection.RemoteIpAddress;
            string ipAddress = ip.ToString();
            bool debug = true; // true for DEBUG
            string status = "entered function";
            TcpClient tcpClient = new TcpClient();

            List<Image> fpList = FingerprintService.authenticateFP("24.84.225.22", 3);

            // Do fingerprint fetch from windows service here
            //List<Image> fpList = FingerprintService.scanMultiFP(ipAddress, 3, out _);
            Image fpImg = null;
            for (int i = 0; i < fpList.Count; i++)
            {
                var debugByte = FingerprintService.imgToByte(fpList[i]);
                fpImg = FingerprintService.byteToImg(debugByte);
                fpImg.Save(i.ToString() + ".bmp");
            }

            //bool compare = FingerprintService.compareFP(fpImg, fpList);
            // Write the Public IP of the client computer on the window
            var model = new TestFingerprintButton()
            {
                //message = "The Public IP address of the client is: " + ipAddress
                message = status
            };

            return RedirectToAction("TestFingerprintButton", model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
