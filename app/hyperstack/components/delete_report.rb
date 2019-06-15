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
    if @failure
      H1 { "No report found with delete key" }
      P { @failure }
      return
    end

    if !@report_info
      H1 { "Fetching report info" }
    else
    
    H1 { "Delete report" }
    H3 { match.params[:delete_key] }
    P { "#{@report_info}"}

    end
  end
end
