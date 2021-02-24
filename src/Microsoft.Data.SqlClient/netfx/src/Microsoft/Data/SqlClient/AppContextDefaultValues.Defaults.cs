// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Data.SqlClient;

namespace System
{
    internal static partial class AppContextDefaultValues
    {
        static partial void PopulateDefaultValuesPartial(string platformIdentifier, string profile, int version)
        {
            // When defining a new switch  you should add it to the last known version.
            // For instance, if you are adding a switch in .NET 4.6 (the release after 4.5.2) you should defined your switch
            // like this:
            //    if (version <= 40502) ...
            // This ensures that all previous versions of that platform (up-to 4.5.2) will get the old behavior by default
            // NOTE: When adding a default value for a switch please make sure that the default value is added to ALL of the existing platforms!
            // NOTE: When adding a new if statement for the version please ensure that ALL previous switches are enabled (ie. don't use else if)
            switch (platformIdentifier)
            {
                case ".NETCore":
                case ".NETFramework":
                    {
                        LocalAppContext.DefineSwitchDefault(LocalAppContextSwitches.MakeReadAsyncBlockingString, true);
                        break;
                    }
            }
        }
    }
}
