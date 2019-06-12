class ReportItem < HyperComponent
  param :report
  render(TR) do
    TD{"#{@Report.id}"}
    TD{"#{@Report.updated_at}"}
    TD{"#{@Report.crash_time}"}
    TD{"#{@Report.created_at}"}
    TD{"#{@Report.description}"}
    TD{"#{@Report.public}"}
    TD{"#{@Report.solved}"}
    TD{"#{@Report.solved_comment}"}
  end
end

class Reports < HyperComponent

  render(DIV) do

    H1 { "Crash reports" }

    TABLE {

      THEAD {
        TR{
          TD{ "ID" }
          TD{ "Updated At" }
          TD{ "Crash Time" }
          TD{ "Created At" }
          TD{ "Description" }
          TD{ "Public" }
          TD{ "Solved" }
          TD{ "Solve comment" }
        }
      }

      TBODY{
        Report.visible_to(Hyperstack::Application.acting_user_id).index_by_updated_at.each{
          |report|
          ReportItem(report: report)
        }
      }
    }    

  end
end
