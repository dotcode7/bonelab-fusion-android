﻿using SLZ.Interaction;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LabFusion.Extensions {
    public static class AlignPlugExtensions {
        public static void ForceEject(this AlignPlug plug) {
            plug.EjectPlug();

            while (plug._isExitTransition)
                plug.Update();
        }
    }
}