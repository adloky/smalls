var upload = (function() {
    $('head').append(`<style>
        .smart-hidden { position: absolute; width: 1px; height: 1px; opacity: 0; overflow: hidden; }
    </style>`);
    
    var on = function(area, onUpload) {
        var $area = $(area);
        $area.append('<input class="smart-hidden" type="file" id="uploadInput" multiple/>');
        var $input = $(`${area} input`);

        $area.on('dragover dragenter', function(e) {
            e.preventDefault(); e.stopPropagation();
            $area.addClass('dragover');
        });

        $area.on('dragleave drop', function(e) {
            e.preventDefault(); e.stopPropagation();
            $area.removeClass('dragover');
        });
        
        $area.on('click', function() {
            $input.trigger('click');
        });
        
        $input.on('click', function(e) {
            e.stopPropagation();
        });
        
        $input.on('change', function() {
            var file = this.files[0];
            uploadFile(file);
        });

        $area.on('drop', function(e) {
            var file = e.originalEvent.dataTransfer.files[0];
            uploadFile(file);
        });
        
        function uploadFile(file) {
            var formData = new FormData();
            formData.append('file', file); 

            $.ajax({
                url: '/sandbox/api/upload',
                type: 'POST', data: formData,
                contentType: false, processData: false,
                success: function (r) {
                    onUpload(r.url);
                }
            });
        }
    }
    
    return on;
})();