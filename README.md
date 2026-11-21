# SFTP Connection Tester

A small Windows desktop application for testing SFTP connectivity using an SSH private key. It reports the server's host-key fingerprints, checks the working directory, and lists its entries without uploading or modifying files.

## Requirements

- Windows
- Visual Studio with the .NET desktop development workload and the .NET Framework 4.8 Developer Pack
- NuGet package restore (the project uses [SSH.NET](https://www.nuget.org/packages/SSH.NET/))

## Build and run

1. Open `SFTP-Client\SFTP-Client.slnx` in Visual Studio.
2. Restore NuGet packages if prompted.
3. Build and run the `SFTP-Client` project.

## Use

1. Enter the SFTP server host, port (default `22`), and username.
2. Select your private key file. Enter its passphrase if the key is encrypted.
3. Click **Test Connection**. If no expected fingerprint is entered, the app displays the server fingerprint and deliberately refuses the connection.
4. Verify the fingerprint through a trusted, independent channel with the server administrator. Enter the verified fingerprint and test again. The app accepts SHA-256 fingerprints (`SHA256:<base64>`) or MD5 fingerprints (colon-separated hexadecimal).

On a successful connection, the app reads the server version, working directory, and directory entry count. It does not save connection settings or retain the passphrase after the application closes. Treat private keys and passphrases as sensitive; never commit them to this repository.
