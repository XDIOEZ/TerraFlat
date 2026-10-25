test.check("world_ready")
for _, water in ipairs({false,true}) do
    test.runtime("industrial_fluids", "engine", {water=water}, function(d)
        assert(d.available>0, "有正式原料的引擎没有能力")
        assert(d.idle==d.before, "引擎空载或停转仍扣料")
        assert(d.after<d.before and d.remainingReservations==0, "有负载未扣料或遗留预约")
        if not water then
            assert(d.airPower==0, "只有氧气被当作免费燃料")
            assert(math.abs((d.before-d.after)-d.oxygenUsed*2)<.000001, "气引擎未按实际氢氧比扣料")
            assert(math.abs(d.waterProduct-(d.before-d.after))<.000001, "氢氧产水不守恒")
        end
    end, water and "地面蒸汽机实际消耗淡水且空载不耗" or "气引擎实际氢氧燃料与产水")
end
