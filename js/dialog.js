var dialog = (function () {
    $('head').append(`
        <style>
            dialog { width: 100%; max-width: 300pt; border: none; padding: 0; }
            dialog > .dialog-content-wrapper { width: 100%; height: 100%; padding: 20pt; }
            dialog::backdrop { background-color: rgba(0, 0, 0, 0.33); }
        </style>
    `);
    
    $(document).ready(function () {
        $("dialog").wrapInner('<div class="dialog-content-wrapper"></div>');
        
        var mouseDownTarget;
        
        $("dialog").click(function(ev) {
            var t = $(ev.target);
            var isDialog = t.is("dialog");
            if (!isDialog && !t.hasClass("dialog-close")) return;
            if (isDialog && mouseDownTarget !== this) return;
            
            t.closest("dialog")[0].close();
        });
    
        $("dialog, .dialog *").on("mousedown", function(ev) {
            mouseDownTarget = ev.target;
        });
    });
    
    
    function bind(trigger, dialog) {
        $(trigger).click(function () {
            $(dialog)[0].showModal();
        });
    }
    
    return bind;
})();