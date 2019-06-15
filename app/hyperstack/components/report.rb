class ReportView < HyperComponent
  include Hyperstack::Router::Helpers
  
  render(DIV) do

    report = Report.find_by_id match.params[:id]

    H1 { "Report #{report.id}" }

    UL {
      LI { "Version: #{report.game_version}" }
      LI { "Updated At" }
      LI { "Solved" }
      LI { "Crash Time" }
      LI { "Description" }
      LI { "Version" }
      LI { "Public" }
      LI { "Solve comment" }    
    }
  end
end
