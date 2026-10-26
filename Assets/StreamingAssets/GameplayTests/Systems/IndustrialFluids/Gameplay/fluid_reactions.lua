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
        else
            local f=d.fuel
            assert(f.slots==3 and f.savedSlots==3, "蒸汽机燃料输入或快照没有保留三个真实槽位")
            assert(not f.emptyIgnition and f.coldPower==0 and f.unlitPower==0,
                "空燃料槽可以点火，或未点火的蒸汽机免费供能")
            assert(not f.acceptsNonFuel and not f.invalidDeposited and f.acceptsTinder,
                "蒸汽机燃料槽未通过正式接收策略拒绝非燃料")
            assert(f.cancelledIgnition and f.removedFuel==0 and not f.removedIgnited and f.removedAmount==2,
                "吸收期间取回火种后仍凭空增加燃值或扣掉原物品")
            assert(f.ignition and f.pendingPower==0 and f.pendingAmount==2
                and f.pendingProgress>0 and f.pendingProgress<1,
                "蒸汽机火种吸收未完成就扣料或提供动力")
            assert(f.absorbedAmount==1 and f.burning and f.before>0 and f.capacity>0,
                "点火未实际扣除一份火种并写入燃料值")
            assert(f.idle==f.before and f.after<f.before,
                "蒸汽机空载扣燃料，或带载耗水时没有同时扣真实燃料值")
            assert(f.extinguished and not f.burningAfterStop and f.stoppedPower==0
                and f.stoppedFuel==f.beforeStop,
                "熄火按钮没有停止动力或丢失燃料储备")
            assert(f.relit and f.dryPower==0 and f.dryAfter==f.dryBefore,
                "重新点火失败，或无水时继续供能和消耗燃料")
            assert(not f.oxygenStarvedIgnited and f.oxygenStarvedFuel==f.dryAfter
                and f.savedFuel==f.oxygenStarvedFuel,
                "缺氧未熄火、清空储备，或快照丢失真实燃料值")
            assert(f.finalIgnition and f.burningBeforeExhaustion, "燃尽边界前置未完成真实点火")
            if f.consumptionRate>0 then
                assert(f.afterExhaustion==0 and not f.ignitedAfterExhaustion,
                    "燃值耗尽且槽内无燃料时没有自动熄火")
            else
                assert(f.afterExhaustion==f.beforeExhaustion, "零消耗规则仍扣燃料")
            end
            assert(not f.rupturedIgnition and not f.rupturedIgnited, "损坏的蒸汽机仍可点火")
        end
    end, water and "蒸汽机三槽点火、吸收、燃料水量随负载结算与熄火保留" or "气引擎实际氢氧燃料与产水")
end

test.runtime("industrial_fluids", "fuel_oil_engine", {}, function(d)
    assert(d.emptyPower == 0 and d.waterPower == 0 and math.abs(d.waterLeft - 1) < 0.000001,
        "燃油机缺油或只接水仍免费供能/扣掉异种液体")
    assert(d.blockedPower == 0 and math.abs(d.blockedFuel - 1) < 0.000001,
        "燃油机绕过关闭阀门取料或扣掉来源")
    assert(d.available > 0 and d.loadedPower > 0 and d.beforeOxygenLossPower > 0,
        "真实燃油罐经液管供给时燃油机没有机械输出")
    assert(d.idle == d.before and d.idleReservations == 0,
        "燃油机空载/停转消耗燃油或遗留预约")
    assert(d.oxygenStarvedPower == 0 and d.oxygenStarved == d.before and d.oxygenStarvedReservations == 0,
        "燃油机缺氧时仍预约、消费燃油或提供动力")
    assert(d.afterOxygenLoss == d.before and d.oxygenLossReservations == 0,
        "燃油机预约后失氧仍消耗燃油或遗留预约")
    assert(d.afterWork < d.before and d.afterWorkReservations == 0 and d.pipeHasNoLiquid,
        "带载燃油机没有扣真实来源燃油，或管道错误储油")
    assert(d.savedFuel == d.afterWork and d.savedReservations == 0,
        "燃油罐快照丢失实际燃油或包含引擎运行预约")
    assert(d.removedReservations == 0 and d.afterRemoval == d.afterWork,
        "拆除燃油机遗留来源预约或删除燃油")
end, "燃油机实际管罐供料、阀门阻挡、无负载/缺氧不扣油、带载消耗与保存清理")
