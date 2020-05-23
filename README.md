# Device Commander (DevCDR)
is a lightweight Client/Server Management tool for Windows published under the Open-Source License "GNU General Public License v3.0".
The Web-based Admin console allows to trigger actions against a single or multiple clients. Just mark the clients and select an activity from the right click menu or a custom PowerShell script.

Summary: https://rzander.azurewebsites.net/device-commander/

Get a free instance:

<a href="https://azuredeploy.net/?repository=https://github.com/rzander/devcdr/tree/ServerCore31" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

> Installation Manual on [Wiki](https://github.com/rzander/DevCDR/wiki/Deploy-to-Azure)

## Features
* interactive Web frontend 
* run PowerShell activities and custom commands in realtime
* Install Software directly from https://RuckZuck.tools
  * Automatically update:  
7-Zip,AdobeAir,AdobeReader DC,AdobeReader DC MUI,Code,FileZilla,Firefox,FlashPlayerPlugin,FlashPlayerPPAPI,GIMP,Google Chrome,	JavaRuntime8,Microsoft Azure Information Protection,Microsoft OneDrive,Microsoft Power BI Desktop,Notepad++,Office Timeline,Putty,	ShockwavePlayer,Slack,Teams Machine-Wide Installer,VCRedist2010,VCRedist2012,VCRedist2013,VCRedist2015,VCRedist2017,VCRedist2019,	Viber,VLC,VSTO2010,WinRAR,WinSCP
 
* Inventory
  * Custom Inventory (PowerShell)
  * Blockchain based archive ([JainDB](https://github.com/rzander/jaindb))
  * Inventory-History with visual Differential and Timeline of changes
  * Custom Reporting with Excel or PowerBI.  [DEMO Report](https://app.powerbi.com/view?r=eyJrIjoiNzUyNDkzNDAtZmFiMC00MGUyLTgyZDUtZmY4ZWZiODAzMjZhIiwidCI6ImVkNDI1ODAyLTExODYtNDRkZS04ODIzLWE0YTU3ZDE0MGEyOCIsImMiOjh9)
* Scheduled PowerShell script for Health- and Compliance-checks
* device grouping
* Only outgoing HTTPS (443) communication (agent) with Proxy support
* Azure AD Authentication (Management-Interface)

![video](https://rzander.azurewebsites.net/content/images/2018/07/devcdr.gif)

## Agent Requirements
* .NET4.7 (Agent for .NET Core is in preview)
* DevCDRAgent (MSI is currently x64 only)

## Server/Host Requirements
* Azure Active Directory (Basic)
* Azure Web App (Free F1 or better)
   * for scaleability you can use managed Azure SignalR Service (https://github.com/rzander/DevCDR/wiki/DevCDR-with-Azure-SignalR-Service)
   * Note: F1 (free) is limited on compute time and the site will stop if the limit is reached.
* SSL

### Docker
Another Options is the preconfigured Docker image available at: https://hub.docker.com/r/zanderr/devcdr_server_core/
or just run:
`docker pull zanderr/devcdr_server_core`

# DEMO
https://devcdr.azurewebsites.net/ 

The default view is in Read-Only mode, so you will not be able to trigger something on my machines :-)
## Test Instance
https://devcdr.azurewebsites.net/DevCDR/Default
You have to install an Agent in this Test-Instance (click the "Agent" button on the Web-Site to get the installation command).

