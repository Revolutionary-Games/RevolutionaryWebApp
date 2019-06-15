# class GetReportInfoByDeleteKey < Hyperstack::ServerOp
#   param :key
#   param :acting_user, allow_nil: true
#   add_error :acting_user, :invalid, "Invalid error" do
#     false
#   end  
#   step {
#     return "going well"
#     report = Report.find_by delete_key: key
#     if !report
#       fail
#     end

#     [report.id, report.created_at, report.description]
#   }
# end


class GetReportInfoByDeleteKey < Hyperstack::ControllerOp
  param :key
  step {
    return "success"
    report = Report.find_by delete_key: key
    if !report
      fail
    end

    [report.id, report.created_at, report.description]
  }
end

