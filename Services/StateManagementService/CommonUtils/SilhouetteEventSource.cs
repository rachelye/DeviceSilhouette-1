﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.Tracing;

//using System.Linq;
//using System.Text;

namespace CommonUtils
{
    [EventSource(Name = "MyCompany-DeviceSilhouette")]
    public class SilhouetteEventSource : EventSource
    {
        public static readonly SilhouetteEventSource Current = new SilhouetteEventSource();

        static SilhouetteEventSource()
        {
            // A workaround for the problem where ETW activities do not get tracked until Tasks infrastructure is initialized.
            // This problem will be fixed in .NET Framework 4.6.2.
            Task.Run(() => { }).Wait();
        }

        // Instance constructor is private to enforce singleton semantics
        private SilhouetteEventSource() : base() { }

        private static class Keywords
        {
            public const EventKeywords SilhouetteException = (EventKeywords)1;
            public const EventKeywords SilhouetteInfo = (EventKeywords)2;

        }

     

        [Event(1, Message = "Silhouette Failure: {0}", Level = EventLevel.Error, Keywords = Keywords.SilhouetteException)]
        public void LogException(string message)
        {
        
            WriteEvent(1, message);
        }

       

        [Event(2, Message = "Silhouette Information: {0}", Level = EventLevel.Informational, Keywords = Keywords.SilhouetteInfo)]
        public void LogInfo(string message) { WriteEvent(2, message); }



    }


}

