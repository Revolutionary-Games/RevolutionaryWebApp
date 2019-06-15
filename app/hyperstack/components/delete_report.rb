class DeleteReport < HyperComponent
  include Hyperstack::Router::Helpers

  before_mount do
    GetReportInfoByDeleteKey.run(key: match.params[:delete_key]).then{|result|
      mutate @report_info = result
    }.fail{|e|
      mutate @failure = e
    }
  end
  
  render(DIV) do
    if @delete_result
      H1 { "#{@delete_result}" }
    end
    
    if @failure
      H1 { "No report found" }
      P { "#{@failure.message}" }
      return
    end

    if !@report_info
      H1 { "Fetching report info" }
    else
      
      H1 { "Delete report #{@report_info[0]}" }
      H3 {
        "Click the link below if you are sure you want to delete your report " +
          "created at #{@report_info[1]} (#{@report_info[2]})"
      }
      P { "It will make it not possible for us Thrive developers to fix this crash!" }

      BUTTON(class: 'button'){ "Click here if you are sure you want to delete." }.on(:click){
        DeleteReportByKey.run(key: match.params[:delete_key]).then{
          mutate @delete_result = "Report deleted"
        }.fail{|e|
          mutate @delete_result = "Failed to delete report"
        }
      }

    end
  end
end
