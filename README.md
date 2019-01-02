# Device Commander (DevCDR)
is a lightweight Client/Server Management tool for Windows published under the Open-Source License "GNU General Public License v3.0".
The Web-based Admin console allows to trigger actions against a single or multiple clients. Just mark the clients and select an activity from the right click menu or a custom PowerShell script.

Summary: https://rzander.azurewebsites.net/device-commander/

Get a free instance:

<a href="https://azuredeploy.net/?repository=https://github.com/rzander/devcdr/tree/ServerCore21" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

> Installation Manual on [Wiki](https://github.com/rzander/DevCDR/wiki/Deploy-to-Azure)

## Features
* interactive Web frontend 
* run PowerShell activities and custom commands in realtime
* Install Software directly from https://RuckZuck.tools
* Inventory
  * Custom Inventory (PowerShell)
  * Blockchain based archive (JainDB)
  * Inventory-History with visual Differential and Timeline of changes
  * Custom Reporting with Excel or PowerBI
* Scheduled PowerShell script for Health- and Compliance-checks
* multi tenancy support (only for Server running .NET 4.7)
* device grouping
* Only outgoing HTTPS (443) communication (agent) with Proxy support
* Azure AD Authentication (Management-Interface)

![video](https://rzander.azurewebsites.net/content/images/2018/07/devcdr.gif)

## Agent Requirements
* .NET4.6
* DevCDRAgent (MSI is currently x64 only)

## Server/Host Requirements
* Azure Active Directory (Basic)
* Azure Web App (Free F1 or better)
* SSL

## Server Core
Server Core is running on .NET Core 2.1. and can only host a single instance.
> Note: Server Core is not compatible with the existing .NET4.6 Agent. please use the new Agent for Server Core.

### Docker
A preconfigured Docker image is available at: https://hub.docker.com/r/zanderr/devcdr_server_core/
or just run:
`docker pull zanderr/devcdr_server_core`

# DEMO
https://devcdr.azurewebsites.net/ 

The default view is in Read-Only mode, so you will not be able to trigger something on my machines :-)
## Test Instance
https://devcdr.azurewebsites.net/DevCDR/Test
You have to install an Agent in this Test-Instance (click the "Agent" button on the Web-Site to get the installation command).

