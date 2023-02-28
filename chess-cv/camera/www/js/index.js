//document.addEventListener('deviceready', function() {
//}, false);
$(document).ready(function() {
    $(".test").click(function () {
        //hub.server.test();
    });
    
    hub = $.connection.cvHub;
    $.connection.hub.url = "https://192.168.0.2:8081/signalr";
      
    $.connection.hub.start().done(function () {
    });
});
