# frozen_string_literal: true

module API
  module V1
    # Single LFS file getting endpoint that allows browsers
    class Launcher < Grape::API
      include API::V1::Defaults

      helpers do
        def fetch_link_code
          code = headers['Authorization']

          error!('Unauthorized', 401) unless code

          code
        end
      end

      resource :launcher do
        desc "Checks if launcher link code is valid, doesn't consume the code"
        params do
        end
        get 'check_link' do
          link_code = fetch_link_code

          {"stuff": "this works now: #{link_code}"}
        end

        desc "Connects launcher"
        params do
        end
        post 'link' do
          link_code = fetch_link_code

          {"stuff": "link with: #{link_code}"}
        end

        desc "Checks launcher connection status"
        params do
        end
        get 'status' do
          link_code = fetch_link_code
          {"stuff": "your status..."}
        end

        desc "Gets currently available devbuild information"
        params do
        end
        get 'status' do
          link_code = fetch_link_code
          {"devbuilds": []}
        end

        desc "Gets info for downloading a devbuild"
        params do
          requires :devbuild, type: Integer, desc: 'Devbuild id'
        end
        get 'status' do
          link_code = fetch_link_code
          {"stuff": "devbuild info"}
        end
      end
    end
  end
end
