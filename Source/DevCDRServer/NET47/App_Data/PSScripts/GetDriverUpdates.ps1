$UpdateSvc = New-Object -ComObject Microsoft.Update.ServiceManager            
$UpdateSvc.AddService2("7971f918-a847-4430-9279-4a52d1efe18d",7,"")  
$Session = New-Object -ComObject Microsoft.Update.Session           
$Searcher = $Session.CreateUpdateSearcher() 
$Searcher.ServiceID = '7971f918-a847-4430-9279-4a52d1efe18d'
$Searcher.SearchScope =  1 # MachineOnly
$Searcher.ServerSelection = 3 # Third Party
$Criteria = "IsInstalled=0 and Type='Driver'"
$SearchResult = $Searcher.Search($Criteria)          
$Updates = $SearchResult.Updates
if($Update.count -gt 0) {
$Updates | select Title, DriverModel, DriverVerDate, Driverclass, DriverManufacturer | convertto-json } else {
	"No Driver-Updates found..."
}