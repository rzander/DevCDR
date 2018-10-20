# Device Commander (DevCDR)
is a lightweight Client/Server Management tool for Windows published under the Open-Source License "GNU General Public License v3.0".
The Web-based Admin console allows to trigger actions against a single or multiple clients. Just mark the clients and select an activity from the right click menu or a custom PowerShell script.

Summary: https://rzander.azurewebsites.net/device-commander/

As soon as the devices have internet connection (also behind a Proxy), they will pop up in the Web-Console. Now can run some predefined commands or any custom PowerShell code and the Devices will respond in Realtime.

## Features
* interactive Web frontend 
* realtime PowerShell activities und custom commands
* Install Software directly from https://RuckZuck.tools
* Inventory
  * Custom Inventory (PowerShell)
  * Blockchain based archive (JainDB)
  * Inventory-History with visual Differential and Timeline of changes
  * Custom Reporting with Excel or PowerBI
* Scheduled PowerShell script forHealth- Compliance-checks
* multi tenancy support
* device grouping
* Only outgoing HTTPS (443) communication (agent) with Proxy support
* Azure AD Authentication (Management-Interface)

## Agent Requirements
* .NET4.6
* DevCDRAgent (MSI is currently x64 only)

## Server/Host Requirements
* Azure Active Directory (Basic)
* Azure Web App (Free F1 or better)
* SSL

# DEMO
https://devcdr.azurewebsites.net/ 

The default view is in Read-Only mode, so you will not be able to trigger something on my machines :-)
## Test Instance
https://devcdr.azurewebsites.net/DevCDR/Test
You have to install an Agent in this Test-Instance (click the "Agent" button on the Web-Site to get the installation command).

