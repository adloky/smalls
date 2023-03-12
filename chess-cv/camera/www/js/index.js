document.addEventListener('deviceready', function() {
    let options = {
        camera: CameraPreview.CAMERA_DIRECTION.BACK,
        toBack: true,
        tapPhoto: false,
    };
    CameraPreview.startCamera(options);
    
    var hub = $.connection.cvHub;
    $.connection.hub.url = "https://192.168.0.2:8081/signalr";
    
    $(".send").click(function() {
        CameraPreview.takeSnapshot({quality: 85}, function(base64) {
            // hub.server.log(("" + base64).length);
            hub.server.sendImage("123");
        })
    });
     
    $.connection.hub.start().done(function () {
        // 1280x720
        /*
        CameraPreview.getSupportedPictureSizes(function(dimensions) {
            dimensions.forEach(function (dimension) {
                hub.server.log(dimension.width + 'x' + dimension.height);
            });
        });
        */
    });
    
}, false);
