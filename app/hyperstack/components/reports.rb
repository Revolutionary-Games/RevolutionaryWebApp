class ReportItem < HyperComponent
  include Hyperstack::Router
  
  param :report
  render(TR) do
    TD{"#{@Report.game_version}"}
    # TD{"#{@Report.id}"}
    TD{Link("/report/#{@Report.id}") { "#{@Report.updated_at}" }}
    TD{@Report.solved ? "yes" : "no"}
    TD{"#{@Report.crash_time}"}
    TD{"#{@Report.description}"}
    TD{"#{@Report.public}"}
    TD{"#{@Report.solved_comment}"}
  end
end

class Reports < HyperComponent  
  render(DIV) do

    H1 { "Crash reports" }

    TABLE {

      THEAD {
        TR{
          TD{ "Version" }
          # TD{ "ID" }
          TD{ "Updated At" }
          TD{ "Solved" }
          TD{ "Crash Time" }
          TD{ "Description" }
          TD{ "Version" }
          TD{ "Public" }
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
