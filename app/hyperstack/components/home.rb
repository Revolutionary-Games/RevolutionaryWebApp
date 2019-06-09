class Home < HyperComponent
  include Hyperstack::Router

  # after_mount do
  #   CurrentUserDetails.run.then do |value|
  #     mutate @user = value
  #   end.fail do |error|
  #     mutate @user = "Error: #{error}"
  #   end    
  # end
  
  render(DIV) do

    H1 { "This is an inprogress Thrive developer hub." }

    H3 { "The Thrive Launcher is now able to create crash reports." }

    P { "If you want to decode a crash dump you can use the link below to go " + 
        "to the plain crash dump processing tool." }

    H3 { Link('/crashdump-tool') { 'Process a crash here' } }

    P {
      "If you are a Thrive developer log in using your Dev Forum account to see more options"
    }
    
  end
end
