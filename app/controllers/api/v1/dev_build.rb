# frozen_string_literal: true

module API
  module V1
    # Devbuild API
    class DevBuild < Grape::API
      include API::V1::Defaults

      helpers do
        def access_key
          code = headers['X-Access-Code']

          return nil if code.blank?

          key = AccessKey.find_by key_code: code

          error!({ error_code: 401, message: 'Invalid access key' }, 401) unless key

          key.update_last_used

          key
        end
      end

      resource :devbuild do
        desc 'Checks if the server wants the specified devbuild'
        params do
          requires :build_hash, type: String, desc: 'Devbuild hash'
          requires :build_platform, type: String, desc: 'Devbuild platform'
        end
        post 'offer_devbuild' do
          key = access_key

          status 501
          { "your key": "is: #{key}" }
        end

        desc 'Checks if the server wants any of the specified dehydrated objects'
        params do
          requires :objects, type: Array, desc: 'Offered objects'
        end
        post 'offer_objects' do
          code = access_key

          status 501
          { "your key": "is: #{code}" }
        end

        desc 'Starts upload of a devbuild. The required objects need to be already uploaded'
        params do
          requires :build_hash, type: String, desc: 'Devbuild hash'
          requires :build_branch, type: String, desc: 'Devbuild branch'
          requires :build_platform, type: String, desc: 'Devbuild platform'
          requires :required_objects, type: Array, desc:
            'List of the objects needed by this build'
        end
        post 'offer_devbuild' do
          code = access_key

          status 501
          { "your key": "is: #{code}" }
        end

        desc 'Starts upload of the specified objects'
        params do
          requires :objects, type: Array, desc: 'Objects to upload'
        end
        post 'upload_objects' do
          code = access_key

          status 501
          { "your key": "is: #{code}" }
        end

        desc 'Report finished devbuild / object upload'
        params do
          requires :token, type: String, desc: "Finished upload's token"
          requires :build_branch, type: String, desc: 'Devbuild branch'
          requires :build_platform, type: String, desc: 'Devbuild platform'
        end
        post 'finish' do
          status 501
        end
      end
    end
  end
end
