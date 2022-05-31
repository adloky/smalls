//document.addEventListener('deviceready', function() {
//}, false);
$(document).ready(function() {
    var vm = {};
    var gamePerRound = ko.observable(2);
    var maxPlayers = 8;
    var autosaveKey ="autosave";
    
    var berger = [];
    
    berger[4] = [
      [[0,3],[1,2]],
      [[3,2],[0,1]],
      [[1,3],[2,0]]
    ];

    berger[6] = [
      [[0,5],[1,4],[2,3]],
      [[5,3],[4,2],[0,1]],
      [[1,5],[2,0],[3,4]],
      [[5,4],[0,3],[1,2]],
      [[2,5],[3,1],[4,0]]
    ];

    berger[8] = [
      [[0,7],[1,6],[2,5],[3,4]],
      [[7,4],[5,3],[6,2],[0,1]],
      [[1,7],[2,0],[3,6],[4,5]],
      [[7,5],[6,4],[0,3],[1,2]],
      [[2,7],[3,1],[4,0],[5,6]],
      [[7,6],[0,5],[1,4],[2,3]],
      [[3,7],[4,2],[5,1],[6,0]]
    ];

    vm.menu = ko.observable(false);
    
    vm.menuClick = function() {
        vm.menu(true);
    }
    
    vm.menuCancelClick = function() {
        vm.menu(false);
    }
    
    /*
    function linkedPair() {
        var a = ko.observable(null);
        var b = ko.observable(null);
        
        function create(obsA, obsB) {
            var result = ko.pureComputed({
                read: function () {
                    return obsA();
                },
                write: function (value) {
                    if (value === null) {
                        obsA(null);
                        obsB(null);
                        return;
                    }
                    
                    value = Number(value);
                    if (value > gamePerRound()) {
                        obsA(null);
                        obsB(null);
                    } else {
                        obsA(value);
                        obsB(gamePerRound()-value);
                    }
                }
            });
            return result;
        }
        
        return [create(a,b),create(b,a)];
    }
    */
    
    function createGrid() {
        var grid = [];
        
        for (var i = 0; i < maxPlayers; i++) {
            var result = { name: ko.observable(""), edit: ko.observable(false), scores: [] };
            for (var j = 0; j < maxPlayers; j++) {
                result.scores.push(null);
            }
            grid.push(result);
        }
        
        for (var j = 0; j < maxPlayers-1; j++) {
            for (var i = j+1; i < maxPlayers; i++) {
                grid[j].scores[i] = ko.observable(null);
                grid[i].scores[j] = ko.observable(null);
            }
        }
        
        for (var i = 0; i < maxPlayers; i++) {
            grid[i].scores[i] = ko.observable("x");
        }
        
        return grid;
    }
    
    vm.grid = createGrid();
    
    vm.scoreView = function(s) {
        if (ko.isObservable(s)) {
            s = s();
        }
        return s === null ? "-" : s;
    }
    
    vm.nameClick = function (_,e) {
        vm.grid.filter(x => x != this).forEach(x => x.edit(false));
        this.edit(true);
        $(e.target).find("input").focus();
    }

    vm.nameBlur = function () {
        this.edit(false);
    }
    
    vm.playerCount = ko.computed(() => {
        var names = vm.grid.map(x => x.name());
        var n = 0;
        for (var i = 0; i < maxPlayers; i++) {
            if (names[i] === "") break;
            n++;
        }
        
        return n;
    });
    
    vm.pairs = ko.computed(() => {
        var result = [];
        var n = vm.playerCount();
        var dn = (n % 2 === 0) ? 0 : 1;
        if (n < 3) {
            return result;
        }

        var ba = berger[n+dn];
        for (var t = 0; t < ba.length; t++) {
            var ta = ba[t];
            result.push({ title: ko.observable("Тур " + (t+1)) });
            for (var p = 0; p < ta.length; p++) {
                var [a,b] = ta[p];
                if (n < Math.max(a,b) + 1) {
                    continue;
                }
                var pair = { title: ko.observable(false), players: [] };
                pair.players.push({ name: vm.grid[a].name, score: vm.grid[a].scores[b] });
                pair.players.push({ name: vm.grid[b].name, score: vm.grid[b].scores[a] });
                result.push(pair);
            }
        }
        
        return result;
    });
    
    vm.scoreClick = function() {
        var s = this.score();
        if (s === null) {
            s = -0.5;
        }
        
        s += 0.5;
        
        this.score(s);
    }

    vm.scoreResetClick = function () {
        this.players[0].score(null);
        this.players[1].score(null);
    }
    
    function save() {
        var gridJson = JSON.stringify(ko.mapping.toJS(vm.grid));
        localStorage.setItem(autosaveKey, gridJson);
    }
    
    function load() {
        var gridJson = localStorage.getItem(autosaveKey);
        if (!gridJson) {
            return;
        }
        
        var gridDto = JSON.parse(gridJson);
        
        try {
            for (var g = 0; g < gridDto.length; g++) {
                var go = gridDto[g];
                vm.grid[g].name(go.name);
                var sa = go.scores;
                for (var s = 0; s < sa.length; s++) {
                    vm.grid[g].scores[s](sa[s]);
                }
            }
        }
        catch (e) {}
    }
    
    vm.reset = function() {
        for (var i = 0; i < maxPlayers; i++) {
            vm.grid[i].name("");
        }
        
        for (var j = 0; j < maxPlayers-1; j++) {
            for (var i = j+1; i < maxPlayers; i++) {
                vm.grid[j].scores[i](null);
                vm.grid[i].scores[j](null);
            }
        }
    }
    
    vm.resetClick = function() {
        vm.reset();
        vm.menu(false);
    }
    /*
    var autosaveTimerId = null;

    var autosaveComp = ko.computed(() => {
        vm.grid.map(x => x.name());
        vm.grid.map(x => x.scores.map(y => y()));
        
        if (autosaveTimerId !== null) {
            clearTimeout(autosaveTimerId);
        }
        
        autosaveTimerId = setTimeout(() => { save(); }, 10 * 1000);
    });
    */
    
    vm.test = function () {
        //save();
    }

    $(document).on("pause", () => {
        save();
    });    
    
    load();    
    ko.applyBindings(vm);
});
