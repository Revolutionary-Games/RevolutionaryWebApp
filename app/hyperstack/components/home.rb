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

    H1 { "This is an inprogress Thrive crash report hub." }

    H3 { Link('/submit') { 'Submit a crash here' } }

    P {
      "If you are a Thrive developer log in using your Dev Forum account to see more options"
    }
    
  end
end
