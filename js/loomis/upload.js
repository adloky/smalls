var upload = (function() { var m = {};    
$(document).ready(function() {
    var $uploadArea = $('#uploadArea');
    var $fileInput = $('#uploadInput');
    var $uploadDialog = $('#uploadDialog');

    $uploadArea.on('dragover dragenter', function(e) {
        e.preventDefault(); e.stopPropagation();
        $uploadArea.addClass('dragover');
    });

    $uploadArea.on('dragleave drop', function(e) {
        e.preventDefault(); e.stopPropagation();
        $uploadArea.removeClass('dragover');
    });

    $uploadArea.on('drop', function(e) {
        var file = e.originalEvent.dataTransfer.files[0];

        var formData = new FormData();
        formData.append('file', file); 

        $.ajax({
            url: '/sandbox/api/upload', // http://localhost
            type: 'POST', data: formData,
            contentType: false, processData: false,
            success: function (r) {
                $uploadArea.addClass("loaded");
                $uploadArea.data("url", r.url);
                $uploadArea.css({ backgroundImage: `url('${r.url}')` });
            }
        });
    });

    $(".upload-dialog div").click(function(e) {
        e.stopPropagation();
    });
    
    $(".upload-dialog button").click(function() {
        if ($(this).text() != "OK") return;
        
        var url = $uploadArea.data("url");
        if (!url) return;
        
        m.onLoad(url);
    });

    $(".upload-dialog button, .upload-dialog").click(function() {
        $uploadDialog.addClass("hidden");
        $uploadArea.removeClass("loaded");
        $uploadArea.data("url", null);
        $uploadArea.css({ backgroundImage: "" });
    });
    
    m.show = function () {
        $uploadDialog.removeClass("hidden");
    }
    
    m.load = function(f) {
        m.onLoad = f;
    }
    
    
}); return m;   
})();