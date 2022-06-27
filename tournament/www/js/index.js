//document.addEventListener('deviceready', function() {
//}, false);
$(document).ready(function() {
    var vm = {};
    var gamePerRound = ko.observable(2);
    var maxPlayers = 10;
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

    berger[10] = [
      [[0,9],[1,8],[2,7],[3,6],[4,5]],
      [[9,5],[6,4],[7,3],[8,2],[0,1]],
      [[1,9],[2,0],[3,8],[4,7],[5,6]],
      [[9,6],[7,5],[8,4],[0,3],[1,2]],
      [[2,9],[3,1],[4,0],[5,8],[6,7]],
      [[9,7],[8,6],[0,5],[1,4],[2,3]],
      [[3,9],[4,2],[5,1],[6,0],[7,8]],
      [[9,8],[0,7],[1,6],[2,5],[3,4]],
      [[4,9],[5,3],[6,2],[7,1],[8,0]]
    ];
    
    
    vm.menu = ko.observable(false);
    
    vm.menuClick = function() {
        vm.menu(true);
    }
    
    vm.menuCancelClick = function() {
        vm.menu(false);
    }
    
    vm.groups = ["A", "B", "C", "D"].map((x,i) => { return { name: x, num: i}});
    vm.groupNumber = ko.observable(0);
    vm.groupClick = function () {
        vm.groupNumber(this.num);
    }
    
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
        
        for (var i = 0; i < maxPlayers; i++) {
            grid[i].sum = ko.computed(function() {
                var scores = this.scores
                    .map(x => x())
                    .filter(x => x !== null && !isNaN(x));
                    
                if (scores.length === 0) {
                    return null;
                }
                
                return scores.reduce((a,b) => a + b, 0);
            }, grid[i]);
        }
        
        return grid;
    }
    
    var grids = vm.groups.map(() => createGrid());    
    vm.grid = ko.computed(() => {
        return grids[vm.groupNumber()];
    });
    
    vm.scoreView = function(s) {
        if (ko.isObservable(s)) {
            s = s();
        }
        return s === null ? "-" : s;
    }
    
    vm.nameClick = function (_,e) {
        vm.grid().filter(x => x != this).forEach(x => x.edit(false));
        this.edit(true);
        $(e.target).find("input").focus();
    }

    vm.nameBlur = function () {
        this.edit(false);
    }
    
    vm.playerCount = ko.computed(() => {
        var names = vm.grid().map(x => x.name());
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
        var grid = vm.grid();
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
                pair.players.push({ name: grid[a].name, score: grid[a].scores[b] });
                pair.players.push({ name: grid[b].name, score: grid[b].scores[a] });
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
        var gridsJson = JSON.stringify(ko.mapping.toJS(grids));
        localStorage.setItem(autosaveKey, gridsJson);
    }
    
    function load() {
        var gridsJson = localStorage.getItem(autosaveKey);
        if (!gridsJson) {
            return;
        }
        
        var gridsDto = JSON.parse(gridsJson);
        
        try {
            for (var p = 0; p < gridsDto.length; p++) {
                var gridDto = gridsDto[p];
                var grid = grids[p];
                for (var g = 0; g < gridDto.length; g++) {
                    var go = gridDto[g];
                    grid[g].name(go.name);
                    var sa = go.scores;
                    for (var s = 0; s < sa.length; s++) {
                        grid[g].scores[s](sa[s]);
                    }
                }
            }
        }
        catch (e) {}
    }
    
    vm.reset = function() {
        for (var k = 0; k < grids.length; k++) {
            var grid = grids[k];
            for (var i = 0; i < maxPlayers; i++) {
                grid[i].name("");
            }
            
            for (var j = 0; j < maxPlayers-1; j++) {
                for (var i = j+1; i < maxPlayers; i++) {
                    grid[j].scores[i](null);
                    grid[i].scores[j](null);
                }
            }
        }
        vm.groupNumber(0);
    }
    
    vm.resetClick = function() {
        vm.reset();
        vm.menu(false);
    }
    
    vm.test = function () {
        //save();
    }

    $(document).on("pause", () => {
        save();
    });    

    $(window).on("beforeunload", () => {
        save();
    });    
    
    load();    
    ko.applyBindings(vm);
});
